"""Export instrument name tables from the V51 firmware image into the app's embedded JSON.

Usage: python export_instruments.py [firmware.bin] [out.json]
Output: {"<bank>": [[name, type, zone], ...]}  index = instrument number.
type/zone are only meaningful for bank 0 (1 kick, 2 snare, 4 tom, 5 hihat, 7 crash, 8 ride, 9 splash, 10 china;
zone 1 head, 2 rim, 3 bow, 4 edge, 5 bell). Offsets are for firmware 2.10.
"""
import json, re, sys

TABLES = {0: 154706148, 2007: 84315836, 2008: 84618196, 2009: 85010396}
STRIDE = 48


def export(fw, out):
    d = open(fw, 'rb').read()
    res = {}
    for bank, off in TABLES.items():
        rows = []
        while re.fullmatch(rb'[ -~]{16}', d[off + STRIDE * len(rows): off + STRIDE * len(rows) + 16]):
            r = d[off + STRIDE * len(rows): off + STRIDE * (len(rows) + 1)]
            rows.append([r[:16].decode('ascii').rstrip(), r[18] if bank == 0 else 0, r[19] if bank == 0 else 0])
        res[str(bank)] = rows
        print(f'bank {bank}: {len(rows)} instruments')
    json.dump(res, open(out, 'w'), separators=(',', ':'))


if __name__ == '__main__':
    export(sys.argv[1] if len(sys.argv) > 1 else '../SystemProgram/v51_sys_v210/V51_UPA.BIN',
           sys.argv[2] if len(sys.argv) > 2 else '../src/KitRoles/Assets/instruments.json')
