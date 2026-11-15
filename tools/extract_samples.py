"""Extract user samples (VUD chunk) from a Roland V51/V71 backup (.TD0) to WAV files.

Usage: python extract_samples.py <backup.TD0> <out_dir> [sample_rate]
Sample data is 16-bit mono PCM; the backup does not store the rate (default 44100).
"""
import os, struct, sys, wave

def chunks(d):
    end = struct.unpack('<H', d[:2])[0]
    for i in range(0x10, end, 16):
        off, ln = struct.unpack('<II', d[i + 8:i + 16])
        yield d[i:i + 4].decode(), d[off:off + ln]

def main(src, out, rate=44100):
    d = open(src, 'rb').read()
    vud = dict(chunks(d))['VUDa'][16:]          # skip chunk header
    assert vud[:4] == b'SMPh', 'unexpected sample block'
    count = struct.unpack('<I', vud[12:16])[0]
    pos = struct.unpack('<I', vud[4:8])[0]      # SMPh header length
    os.makedirs(out, exist_ok=True)
    for _ in range(count):
        assert vud[pos:pos + 4] == b'SMPd', f'bad entry at {pos}'
        hlen, chans = struct.unpack('<HH', vud[pos + 4:pos + 8])
        size, frames = struct.unpack('<II', vud[pos + 8:pos + 16])
        name = vud[pos + 16:pos + 32].split(b'\0')[0].decode('latin1')
        slot = struct.unpack('<H', vud[pos + 32:pos + 34])[0] & 0x7fff
        pcm = vud[pos + hlen:pos + hlen + frames * 2 * chans]
        path = os.path.join(out, f'{slot + 1:03d}_{name}')
        with wave.open(path, 'wb') as w:
            w.setnchannels(chans); w.setsampwidth(2); w.setframerate(rate)
            w.writeframes(pcm)
        print(f'slot {slot + 1:3d}  {name:20s} {frames:7d} frames  {frames / rate:.2f}s -> {path}')
        pos += hlen + size

if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 44100)
