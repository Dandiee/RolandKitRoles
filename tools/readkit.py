"""Read one kit fully (read-only) and save raw blocks to JSON. Usage: python readkit.py [kit#|current] [out.json]"""
import json, sys, time
from v51midi import V51, nib, to7
from kitmap import kit_base, kit_blocks, UNITS

def read_kit(v, n):
    base = kit_base(n); out = {}; fails = []; times = []
    for nm, off, sz in kit_blocks():
        d, msgs, dt = v.read(to7(base + off), sz, timeout=1.0)
        times.append(dt)
        if d is None: fails.append(nm)
        else: out[nm] = d.hex()
    return out, fails, times

def summary(out):
    c = bytes.fromhex(out['common'])
    print('name:', c[:16].decode('ascii', 'replace'), '| sub:', c[16:80].decode('ascii', 'replace').rstrip())
    for i, u in enumerate(UNITS):
        uc = bytes.fromhex(out[f'uc{i+1}'])
        row = []
        for L in 'ABC':
            l = bytes.fromhex(out[f'{L}{i+1}'])
            row.append(f"{L}:{'on ' if l[0] else 'off'} inst={nib(l,1):5d} bank={nib(l,5):3d}")
        print(f'{u:7s} mode={uc[0]}  ' + '  '.join(row))

if __name__ == '__main__':
    with V51() as v:
        arg = sys.argv[1] if len(sys.argv) > 1 else 'current'
        n = nib(v.read(bytes(4), 4)[0], 0) + 1 if arg == 'current' else int(arg)
        t0 = time.perf_counter()
        out, fails, times = read_kit(v, n)
        tot = time.perf_counter() - t0
    print(f'kit {n}: {len(out)} blocks ok, failed {fails}; {tot:.2f}s total, '
          f'block min {min(times)*1000:.1f} / max {max(times)*1000:.1f} ms, {sum(len(x)//2 for x in out.values())} bytes')
    json.dump({'kit': n, 'blocks': out}, open(sys.argv[2] if len(sys.argv) > 2 else f'../scratch/kit{n:03d}.json', 'w'))
    if not fails: summary(out)
