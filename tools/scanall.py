"""Read-only full scan of all 200 kits -> ../scratch/kits/kitNNN.json (skips existing files)."""
import json, os, sys, time
from v51midi import V51
from readkit import read_kit
os.makedirs('../scratch/kits', exist_ok=True)
t0 = time.perf_counter()
with V51() as v:
    for n in range(1, 201):
        p = f'../scratch/kits/kit{n:03d}.json'
        if os.path.exists(p): continue
        out, fails, times = read_kit(v, n)
        json.dump({'kit': n, 'blocks': out, 'fails': fails}, open(p, 'w'))
        print(f'kit {n:3d} ok={len(out)} fails={fails} {time.perf_counter()-t0:7.1f}s', flush=True)
print('done', time.perf_counter() - t0)
