"""Kit block layout from the MIDI implementation (offsets written as in the PDF, 7-bit hex)."""
from v51midi import from7, to7

def H(x):  # PDF-style hex address -> linear 7-bit integer
    return from7(x.to_bytes(4, 'big'))

UNITS = ['KICK','SN H','SN R','T1 H','T1 R','T2 H','T2 R','T3 H','T3 R','T4 H','T4 R','HH H','HH R',
         'CR1 H','CR1 R','CR2 H','CR2 R','RD H','RD E','RD B','AUX1 H','AUX1 R','AUX2 H','AUX2 R',
         'AUX3 H','AUX3 R','AUX4 H','AUX4 R']

def kit_base(n):
    return H(0x04000000) + (n - 1) * H(0x040000)

def kit_blocks():
    yield 'common', H(0x000000), H(0x141)
    yield 'midi', H(0x000200), H(0x154)
    yield 'mcomp', H(0x000400), H(0x46)
    yield 'meq', H(0x000500), H(0x2A)
    for i in range(8):  yield f'fx{i+1}', H(0x001000) + i * H(0x0200), H(0x102)
    for i in range(28): yield f'uc{i+1}', H(0x002000) + i * H(0x0100), H(0x34)
    yield 'xstick', H(0x004000), H(0x151)
    for L, s in (('A', 0x005000), ('B', 0x011000), ('C', 0x015000)):
        for i in range(28): yield f'{L}{i+1}', H(s) + i * H(0x0200), H(0x151)
    for i in range(14): yield f'pad{i+1}', H(0x021000) + i * H(0x0100), H(0x47)
    for i in range(4):  yield f'bus{i+1}', H(0x022000) + i * H(0x0100), H(0x1C)
    yield 'room', H(0x023000), H(0x12E)
    yield 'oh', H(0x025000), H(0x113)
    yield 'rev', H(0x027000), H(0x66)
    yield 'busrev', H(0x030000), H(0x66)
    yield 'reso', H(0x031000), H(0x49)
