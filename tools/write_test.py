"""One-off WRITE test on a scratch kit (default 200): change one value, verify, restore, verify.
Usage: python write_test.py [kit]"""
import ctypes as C, sys, time
from v51midi import V51, MIDIHDR, MHDR_DONE, MODEL, checksum, to7, nib, winmm
from kitmap import kit_base, kit_blocks, H

def dt1(v, address, data, dev=0x10):
    body = bytes(address) + bytes(data)
    msg = bytes([0xF0, 0x41, dev]) + MODEL + bytes([0x12]) + body + bytes([checksum(body), 0xF7])
    assert len(data) <= 256
    buf = C.create_string_buffer(msg, len(msg))
    h = MIDIHDR(lpData=C.cast(buf, C.c_void_p), dwBufferLength=len(msg))
    winmm.midiOutPrepareHeader(v.hout, C.byref(h), C.sizeof(h))
    winmm.midiOutLongMsg(v.hout, C.byref(h), C.sizeof(h))
    while not h.dwFlags & MHDR_DONE: time.sleep(0.0005)
    winmm.midiOutUnprepareHeader(v.hout, C.byref(h), C.sizeof(h))
    time.sleep(0.05)  # >= 20 ms between packets

def set_nib(b, off, value, n=4):
    v = value & ((1 << 4 * n) - 1)
    for i in range(n): b[off + i] = (v >> 4 * (n - 1 - i)) & 0x0F

kit = int(sys.argv[1]) if len(sys.argv) > 1 else 200
off, size = {n: (o, s) for n, o, s in kit_blocks()}['A27']  # AUX4 head, layer A
addr = to7(kit_base(kit) + off)
with V51() as v:
    cur = nib(v.read(bytes(4), 4)[0], 0) + 1
    name = v.read(to7(kit_base(kit)), 16)[0]
    print(f'current kit {cur}; target kit {kit} ({name.decode().strip()}), block {addr.hex(" ")} size {size}')
    orig, _, _ = v.read(addr, size)
    vol = nib(orig, 0x09, 4, True)
    mod = bytearray(orig); set_nib(mod, 0x09, vol - 10)
    print(f'layer volume {vol / 10:+.1f} dB -> {(vol - 10) / 10:+.1f} dB')
    dt1(v, addr, mod)
    back, _, _ = v.read(addr, size)
    print('after write:', 'MATCH' if back == bytes(mod) else 'DIFFERENT', f'(volume now {nib(back, 0x09, 4, True) / 10:+.1f} dB)')
    print('bytes differing from intended:', [i for i in range(size) if back[i] != mod[i]][:20])
    dt1(v, addr, orig)
    back2, _, _ = v.read(addr, size)
    print('after restore:', 'MATCH original' if back2 == orig else 'DIFFERENT!')
    cur2 = nib(v.read(bytes(4), 4)[0], 0) + 1
    print('current kit still', cur2)
