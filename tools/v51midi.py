"""Minimal READ-ONLY V51 SysEx access via WinMM (ctypes, no dependencies).

Only Identity Request and RQ1 (data request) messages can be sent. There is deliberately
no DT1 (write) support in this module.
"""
import ctypes as C
import time
from ctypes import wintypes as W

winmm = C.windll.winmm
MHDR_DONE = 0x1
CALLBACK_NULL = 0
MODEL = bytes([0x01, 0x06, 0x01])


class MIDIHDR(C.Structure):
    _fields_ = [('lpData', C.c_void_p), ('dwBufferLength', W.DWORD), ('dwBytesRecorded', W.DWORD),
                ('dwUser', C.c_size_t), ('dwFlags', W.DWORD), ('lpNext', C.c_void_p),
                ('reserved', C.c_size_t), ('dwOffset', W.DWORD), ('dwReserved', C.c_size_t * 8)]


class _Caps(C.Structure):
    _fields_ = [('wMid', C.c_ushort), ('wPid', C.c_ushort), ('ver', C.c_uint), ('name', C.c_wchar * 32)]


def _find(get_num, get_caps, caps_size, name):
    for i in range(get_num()):
        c = (C.c_byte * caps_size)()
        get_caps(i, C.byref(c), caps_size)
        if name in _Caps.from_buffer(c).name:
            return i
    raise RuntimeError(f'MIDI port containing {name!r} not found')


def checksum(body):
    return (128 - sum(body) % 128) % 128


def to7(n, width=4):
    """Integer -> big-endian 7-bit bytes (Roland address/size encoding)."""
    out = []
    for _ in range(width):
        out.append(n & 0x7F)
        n >>= 7
    return bytes(reversed(out))


def from7(b):
    n = 0
    for x in b:
        n = (n << 7) | x
    return n


def addr(*parts):
    """Add Roland 7-bit addresses, e.g. addr(kit_base, 0x005200, 0x21) with hex-written offsets."""
    total = 0
    for p in parts:
        total += from7(p.to_bytes(4, 'big'))
    return to7(total)


def rq1(address: bytes, size: int, dev=0x10):
    body = bytes(address) + to7(size)
    msg = bytes([0xF0, 0x41, dev]) + MODEL + bytes([0x11]) + body + bytes([checksum(body), 0xF7])
    assert msg[6] == 0x11  # never anything but a request
    return msg


class V51:
    def __init__(self, name='V51', nbuf=32, bufsize=1024):
        self.hin, self.hout = W.HANDLE(), W.HANDLE()
        i = _find(winmm.midiInGetNumDevs, winmm.midiInGetDevCapsW, 80, name)
        o = _find(winmm.midiOutGetNumDevs, winmm.midiOutGetDevCapsW, 84, name)
        self._chk(winmm.midiInOpen(C.byref(self.hin), i, 0, 0, CALLBACK_NULL))
        self._chk(winmm.midiOutOpen(C.byref(self.hout), o, 0, 0, CALLBACK_NULL))
        self.bufs = []
        for _ in range(nbuf):
            data = C.create_string_buffer(bufsize)
            h = MIDIHDR(lpData=C.cast(data, C.c_void_p), dwBufferLength=bufsize)
            self._chk(winmm.midiInPrepareHeader(self.hin, C.byref(h), C.sizeof(h)))
            self._chk(winmm.midiInAddBuffer(self.hin, C.byref(h), C.sizeof(h)))
            self.bufs.append((h, data))
        self._chk(winmm.midiInStart(self.hin))
        self._partial = b''

    @staticmethod
    def _chk(r):
        if r:
            raise RuntimeError(f'winmm error {r}')

    def close(self):
        winmm.midiInStop(self.hin)
        winmm.midiInReset(self.hin)
        for h, _ in self.bufs:
            winmm.midiInUnprepareHeader(self.hin, C.byref(h), C.sizeof(h))
        winmm.midiInClose(self.hin)
        winmm.midiOutClose(self.hout)

    def __enter__(self):
        return self

    def __exit__(self, *a):
        self.close()

    def send(self, msg: bytes):
        assert msg[0] == 0xF0 and msg[-1] == 0xF7
        assert msg[1] == 0x7E or msg[6] == 0x11, 'read-only: only Identity Request / RQ1 allowed'
        data = C.create_string_buffer(msg, len(msg))
        h = MIDIHDR(lpData=C.cast(data, C.c_void_p), dwBufferLength=len(msg))
        self._chk(winmm.midiOutPrepareHeader(self.hout, C.byref(h), C.sizeof(h)))
        self._chk(winmm.midiOutLongMsg(self.hout, C.byref(h), C.sizeof(h)))
        while not h.dwFlags & MHDR_DONE:
            time.sleep(0.0005)
        winmm.midiOutUnprepareHeader(self.hout, C.byref(h), C.sizeof(h))

    def poll(self):
        """Return list of complete SysEx messages received so far."""
        out = []
        for h, data in self.bufs:
            if h.dwFlags & MHDR_DONE:
                chunk = C.string_at(data, h.dwBytesRecorded)
                h.dwFlags &= ~MHDR_DONE
                winmm.midiInAddBuffer(self.hin, C.byref(h), C.sizeof(h))
                self._partial += chunk
                while 0xF7 in self._partial:
                    end = self._partial.index(0xF7) + 1
                    m = self._partial[:end]
                    self._partial = self._partial[end:]
                    if m and m[0] == 0xF0:
                        out.append(m)
        return out

    def recv(self, timeout=1.0, until=None):
        msgs, t0 = [], time.perf_counter()
        while time.perf_counter() - t0 < timeout:
            msgs += self.poll()
            if until and until(msgs):
                break
            time.sleep(0.001)
        return msgs

    def identity(self, dev=0x7F):
        self.send(bytes([0xF0, 0x7E, dev, 0x06, 0x01, 0xF7]))
        return self.recv(1.0, until=lambda m: any(x[1] == 0x7E for x in m))

    def read(self, address: bytes, size: int, timeout=2.0):
        """RQ1 one block; returns (data bytes, list of DT1 messages, seconds). Reassembles multi-packet replies."""
        self.poll()  # drain
        t0 = time.perf_counter()
        self.send(rq1(address, size))
        got = {}

        def done(msgs):
            for m in msgs:
                if len(m) > 12 and m[1] == 0x41 and m[3:6] == MODEL and m[6] == 0x12:
                    got[from7(m[7:11])] = m
            return sum(len(m) - 13 for m in got.values()) >= size

        self.recv(timeout, until=done)
        dt = time.perf_counter() - t0
        base = from7(address)
        buf = bytearray(size)
        for a, m in got.items():
            body = m[7:-2]
            if checksum(body) != m[-2]:
                raise RuntimeError(f'bad checksum in reply at {to7(a).hex(" ")}')
            off = a - base
            buf[off:off + len(m) - 13] = m[11:-2]
        n = sum(len(m) - 13 for m in got.values())
        return (bytes(buf) if n >= size else None), list(got.values()), dt


def nib(data, off, n=4, signed=False):
    """Decode n nibble-packed bytes (0000xxxx each)."""
    v = 0
    for b in data[off:off + n]:
        v = (v << 4) | (b & 0x0F)
    if signed and v >= 1 << (4 * n - 1):
        v -= 1 << (4 * n)
    return v
