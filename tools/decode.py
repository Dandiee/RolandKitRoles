"""Decode raw kit blocks (from readkit/scanall JSON) and map instruments to firmware names."""
import json, re
from v51midi import nib
from kitmap import UNITS

FW = '../SystemProgram/v51_sys_v210/V51_UPA.BIN'
CORE_TABLE = 154706148  # offset of bank-0 instrument table in firmware 2.10 (265 x 48-byte records)
TYPES = {1: 'KICK', 2: 'SNARE', 4: 'TOM', 5: 'HIHAT', 7: 'CRASH', 8: 'RIDE', 9: 'SPLASH', 10: 'CHINA'}
ZONES = {0: '', 1: 'head', 2: 'rim', 3: 'bow', 4: 'edge', 5: 'bell'}


def load_core_names(path=FW):
    d = open(path, 'rb').read()
    out = {}
    for k in range(265):
        r = d[CORE_TABLE + 48 * k: CORE_TABLE + 48 * k + 48]
        out[k] = {'name': r[:16].decode('ascii').rstrip(), 'group': r[16], 'type': r[18], 'zone': r[19]}
    return out


# KitUnitLayer fields: (offset, nibbles or 0 for plain byte, signed, name)
LAYER = [(0x00, 0, False, 'InstSw'), (0x01, 4, False, 'Inst'), (0x05, 4, False, 'Bank'),
         (0x09, 4, True, 'Volume'), (0x0D, 0, False, 'TransSw'), (0x0E, 0, False, 'TransAtkTime'),
         (0x0F, 2, True, 'TransAtkDepth'), (0x11, 2, True, 'TransRelDepth'), (0x13, 2, True, 'TransGain'),
         (0x15, 0, False, 'UserSmpTransType'), (0x16, 0, False, 'TransAtkType'), (0x17, 4, True, 'Pitch'),
         (0x1B, 0, False, 'Decay'), (0x1C, 2, True, 'PitchSweep'), (0x1E, 0, False, 'DynEnh'),
         (0x1F, 0, False, 'Dynamics'), (0x20, 0, False, 'Threshold'), (0x21, 0, False, 'EQSw'),
         (0x2C, 0, False, 'VeloLo'), (0x2D, 0, False, 'VeloHi'), (0x2E, 0, False, 'FadeIn'),
         (0x2F, 0, False, 'FadeOut')] + [(0x31 + 4 * i, 4, True, f'V{i+1}') for i in range(40)]


def decode_layer(b):
    out = {}
    for off, n, signed, name in LAYER:
        out[name] = b[off] if n == 0 else nib(b, off, n, signed)
    return out


def load_kit(path):
    j = json.load(open(path))
    return j['kit'], {k: bytes.fromhex(v) for k, v in j['blocks'].items()}
