# Development notes

Everything known about the project: the goal, how the Roland V51 works over MIDI, what was verified on real
hardware, how the app is built, and what is still open. Read this before changing anything.

Facts marked **verified** were tested against a real V51 (system program 2.10). Everything else is inferred.

## 1. Goal and scope

The user owns a Roland TD-516 kit with a V51 module and extra pads and cymbals on the AUX inputs. The V51 stores a
sound per pad per kit and has no global "AUX2 is a China everywhere" setting. When learning songs, the user wants
to switch what the AUX pads play across many kits at once, including downloaded Roland Cloud kits.

Scope of the app, as agreed with the user:

- Read kits from the module, cache them, detect which kits changed.
- Group kits (built-in, instrument expansion, kit pack, blank user kit) and let the user check kits or groups.
- Assign simple roles to AUX1 to AUX4 (tom, snare, crash, ride, China, splash, closed hi-hat) and apply them to the
  checked kits, with preview, backup, verification and undo.
- Out of scope for now: detailed layer editing, editing main pads, anything beyond the AUX pads. The user explicitly
  does not want a "full blown layering solution". Hi-hat position is the only special case.

## 2. Repository layout

| Path | Content |
|---|---|
| `src/KitRoles/` | The WPF app (.NET 10) |
| `src/KitRoles/Assets/instruments.json` | Instrument names extracted from the V51 firmware (generated, see 6.3) |
| `tools/` | Python scripts used to investigate the module; still handy for experiments |
| `docs/V51_MIDI_Implementation_eng01_W.pdf` | Roland's official MIDI implementation, v2.00. Source of truth for the protocol |
| `docs/DEVELOPMENT.md` | This file |

Not in the repository (ignored): the V51 firmware image (`SystemProgram/v51_sys_v210/V51_UPA.BIN`, 160 MB, needed
only to regenerate `instruments.json`), module backups (`*.TD0`), and `scratch/` output of the Python tools.

## 3. Module concepts (terminology)

- **Kit**: one of 200 memory spaces (001 to 200), selected with the big dial. A kit is a recipe, not audio: for each
  of 28 pad inputs and 3 layers (A, B, C) it stores which instrument to play plus volume, pitch, decay, EQ, effect
  sends and 40 "V-EDIT" values.
- **Instrument**: the actual sound. Identified by `(bank, number)`.
- **Slot**: 6 storage places for downloaded Roland Cloud content (instrument expansions and kit packs). Installing
  kits from a slot copies kit recipes into chosen kit spaces; the audio stays in the slot.
- **Instrument expansion** (e.g. DW kits): mostly new sounds plus a few demo kits.
- **Kit pack** ("Inspired by ..."): mostly kit recipes using built-in sounds plus a few pack specific samples.
- **Built-in kits**: factory kits. After a factory reset: kits 1 to 70. Kits named "User Kit" are blank.

Instrument banks seen in real data (**verified**):

| Bank | Meaning | Names available |
|---|---|---|
| 0 | Built-in core sounds (265 entries, 0 = OFF) | yes, firmware table |
| 1, 2 | Probably user samples | no |
| 2007, 2008, 2009 | Preinstalled expansions (vintage, TR-808/909 etc., brushes) | yes, firmware tables |
| 2012/2013, 2034/2035 | Two DW instrument expansions, each a "Drummer Position" + "Producer Position" pair | no |
| 307 to 313 | "Inspired by" kit packs | no |

Bank numbers are IDs, not slot numbers (they are not 1 to 6). They are not `2000 + EXV catalog number` either
(EXV012 in Roland Cloud is "V-Drums Legacy Vol 03", not a DW pack). Moving a pack to a different slot has not been
tested.

The DW expansions only replace drums; their kits use built-in cymbals and hi-hats. Kit packs put their own sound
mostly on AUX1. The V51 has exactly three built-in Chinas (`Mn Bz Trad Ch`, `Zd C-B 1 Ch`, `Zd C-B 2 Ch`) and four
splashes. Every China found in any factory, DW or kit pack kit was one of the built-in three.

Name abbreviations: Mn = Meinl, Zd = Zildjian, Ps = Paiste, Sb = Sabian, Ib Agp = Istanbul Agop; Ch = China,
Sp = splash, Cr = crash, Rd = ride, HH = hi-hat, trailing E = edge.

## 4. MIDI protocol

### 4.1 Connection

- USB MIDI port name "V51" (WinMM input and output). Windows 11 MIDI ports are multi-client.
- The V51 also exposes a USB CDC serial port (USB PID 0331, e.g. COM5). Undocumented, unused.
- Identity Request `F0 7E 7F 06 01 F7` returns `F0 7E 10 06 02 41 01 06 02 00 00 02 01 00 F7`
  (device ID 10h, software revision 2.10). **Verified.**

### 4.2 SysEx messages

Roland model ID `01 06 01`, device ID `10h`.

- RQ1 (read): `F0 41 10 01 06 01 11 aa bb cc dd ss tt uu vv sum F7`
- DT1 (data / write): `F0 41 10 01 06 01 12 aa bb cc dd <data> sum F7`
- Checksum: `(128 - (sum of address and data/size bytes) % 128) % 128`.
  Test vector from the PDF: `F0 41 10 01 06 01 11 04 02 11 0D 00 00 00 01 5B F7`.

### 4.3 The one thing that will bite you: 7-bit sizes

Addresses **and block sizes** in the PDF are 7 bits per byte. "Total Size `00 00 01 51`" means 1 * 128 + 81 = 209
bytes, not 0x151 = 337. With the correct sizes, every kit block fits in a single DT1 packet (largest: KitMidi, 212
bytes). Use `KitMap.H(0x...)` (C#) or `kitmap.H()` (Python) to convert PDF notation to linear values.

Over-sized requests are not rejected: the module simply keeps sending following blocks, even into the next kits.
Never rely on the size field as a limit.

### 4.4 Address map used by the app

| Block | Address (PDF notation) | Size |
|---|---|---|
| Current kit number | `00 00 00 00` | 4 nibbles, 0 to 199 |
| Kit n base | `04 00 00 00` + (n-1) * `04 00 00` (kit 200 = `0A 1C 00 00`) | |

Offsets within a kit (148 blocks total, see `KitMap.KitBlocks`):

| Name in code | Offset | Size | Content |
|---|---|---|---|
| `common` | `00 00 00` | 193 | Kit name (16 ASCII), sub name (64 ASCII), volume, color, etc. Used as the "header" |
| `midi` | `00 02 00` | 212 | Note numbers, gate times, channels |
| `mcomp`, `meq` | `00 04 00`, `00 05 00` | 70, 42 | Master comp, EQ |
| `fx1`..`fx8` | `00 10 00` + i * `02 00` | 130 | Bus effects |
| `uc1`..`uc28` | `00 20 00` + i * `01 00` | 52 | KitUnitCommon per input (layer mode, volume, mute groups, pad EQ, sends) |
| `xstick` | `00 40 00` | 209 | Cross-stick layer |
| `A1`..`A28`, `B..`, `C..` | `00 50 00` / `01 10 00` / `01 50 00` + i * `02 00` | 209 | KitUnitLayer A/B/C per input |
| `pad1`..`pad14` | `02 10 00` + i * `01 00` | 71 | KitPad (pan, bus routing, pad compressor) |
| `bus1`..`bus4`, `room`, `oh`, `rev`, `busrev`, `reso` | see `KitMap.cs` | | Ambience and buses |

Input (unit) index, 1-based: 1 kick, 2/3 snare head/rim, 4/5 tom1, 6/7 tom2, 8/9 tom3, 10/11 tom4,
12/13 hi-hat bow/edge, 14/15 crash1, 16/17 crash2, 18 ride, 19 ride edge, 20 ride bell, 21/22 AUX1, 23/24 AUX2,
25/26 AUX3, 27/28 AUX4. KitPad index: 1 kick, 2 snare, 3-6 tom1-4, 7 hi-hat, 8/9 crash, 10 ride, 11-14 AUX1-4.

KitUnitLayer fields: `00` inst switch, `01-04` instrument (4 nibbles), `05-08` bank (4 nibbles), `09-0C` volume,
`31 + 4*(k-1)` V-EDIT parameter k (4 nibbles each, k = 1..40).

Nibble-packed values (`#` in the PDF) are big-endian 4-bit groups; negative values are two's complement
(**verified**: layer volume 60 = +6.0 dB, V-EDIT -5).

### 4.5 Timing (**verified**)

The module answers at roughly MIDI cable speed, about 3 KB/s. Cost is proportional to bytes:

| Read | Time |
|---|---|
| One layer block (209 B) | ~75 ms |
| One kit header (193 B) | ~72 ms |
| All 200 headers | ~15 s |
| One full kit (148 blocks, 22.5 KB) | ~8 s |
| All 200 kits | ~27 min |

Requesting many blocks with one RQ1 works but is no faster, so the app uses documented per-block reads.
Over 200 full kit reads there were zero failed blocks.

### 4.6 Writing (**verified**)

- A DT1 with a full block to a kit that is **not** currently selected is applied immediately; read-back matches;
  the current kit is unaffected. Tested with `tools/write_test.py` on blank kit 200, then restored.
- The app waits 50 ms after each DT1 (the PDF requires at least 20 ms between packets).
- The module may adjust dependent values itself. Example: writing a hi-hat sound with V-EDIT 2 = 2 (closed) onto an
  AUX pad stored V-EDIT 1 as 24 instead of the sent 26. Verification therefore checks the identity fields strictly
  and accepts other adjustments (see 7.4).
- Unknown: whether SysEx writes survive a power cycle without an explicit save. Must be tested.

### 4.7 Hi-hat sounds on other pads

V-EDIT parameter 2 of a hi-hat instrument is its fixed position on non-hi-hat pads. It is always 0 on the real
hi-hat pad (pedal controlled). **2 = closed** (verified by the user on kit 3 AUX2). Value 1 was seen once (meaning
unconfirmed, probably half open or pressed). 0 on factory AUX hi-hats, presumably open/default.

## 5. Kit classification

`KitClassifier.Classify(name, subName, banks)`:

1. Name `User Kit` means blank user kit.
2. Any bank >= 2010 means instrument expansion.
3. Any bank 300 to 399, or a sub name starting with `Inspired by`, means kit pack.
4. Otherwise built-in. A user made kit built from built-in sounds therefore counts as built-in (intended: the
   category describes where the sounds come from).

With a deep scan, all banks of the kit are used. Without one, the app probes only three layer blocks (kick,
snare and AUX1 layer A, about 0.2 s per kit). Against a full scan, the probe classified 196 of 200 kits correctly;
the misses were user edited copies of pack kits, mostly caught by the sub name rule. After the factory reset the
probe got all 200 right (1-70 built-in, 71-84 expansion, 85-104 kit packs, 105-200 user kits).

Header-only signals (kit color, bus send levels) also differ between groups but can be changed by the user, so
they are not used.

## 6. Instrument names

### 6.1 Where they come from

The firmware update image contains plain-text instrument tables: 48-byte records, 16 ASCII name characters first,
index = instrument number. Offsets for system program 2.10:

| Bank | Offset | Records |
|---|---|---|
| 0 | 154706148 | 265 |
| 2007 | 84315836 | 31 |
| 2008 | 84618196 | 713 |
| 2009 | 85010396 | 23 |

All four were checked against real kits (e.g. kit "TR-808" plays bank 2008 #97 = "TR-808 Kick 1"). Similar
looking tables at other offsets (a 795-record table, duplicate EXV tables) do not match the module's numbering.

### 6.2 Type information (bank 0 only)

Record byte 18 is the instrument type: 1 kick, 2 snare, 4 tom, 5 hi-hat, 7 crash, 8 ride, 9 splash, 10 China.
Byte 19 is the zone: 1 head, 2 rim, 3 bow, 4 edge, 5 bell. This is what makes "find this kit's China" possible.
Expansion tables use a different header scheme; their types are not decoded.

### 6.3 Regenerating

`python tools/export_instruments.py <V51_UPA.BIN> src/KitRoles/Assets/instruments.json`. Offsets are firmware
version specific; for a new firmware, search for the record pattern again (see the script).

### 6.4 Not available

Names of downloaded expansions and kit packs are not in the firmware and not on the PC (Roland Cloud Manager
transfers them straight to the module; its catalog database only has product names). Possible future sources:
the V51 Editor (available in Roland Cloud, not installed), the expansion download packages, or user labels.

## 7. The app

### 7.1 Stack and conventions

- .NET 10, WPF, MVVM with CommunityToolkit.Mvvm (source generators, `[ObservableProperty]`, `[RelayCommand]`).
- Built-in WPF dark theme (`ThemeMode="Dark"`, warning WPF0001 suppressed). Icons from the Segoe Fluent Icons font.
- MIDI via Melanchall.DryWetMidi.
- Keep the code small; no unit tests (user decision). No heavy UI graphics, but icons and status indicators.
- Portable: single self-contained exe, data stored next to it.
- Writing style for user facing text and docs: plain, no em dashes, no emoji.

### 7.2 Files

| File | Role |
|---|---|
| `Midi/KitMap.cs` | Address map, 7-bit conversion, nibble encode/decode, list of the 148 kit blocks, unit names |
| `Midi/V51Client.cs` | DryWetMIDI wrapper: Identity, `ReadAsync` (RQ1, 1 s timeout, one retry, checksum check), `WriteAsync` (DT1 + 50 ms). One request at a time via a semaphore |
| `Model/InstrumentCatalog.cs` | Loads embedded `instruments.json`; name, type, bank labels, lookup by name |
| `Model/KitSnapshot.cs` | Raw blocks of a kit (hex strings), decoding of names and layers, category from all banks |
| `Model/KitCategory.cs` | Category enum and the classification rule |
| `Model/KitCache.cs` | JSON persistence in `data/`: kit snapshots, header index, apply backups |
| `Model/RolePlanner.cs` | Roles, pad references, which blocks to copy and how to patch them |
| `ViewModels/MainViewModel.cs` | Connect, refresh, deep scan, apply, undo, selection, preview, per-kit error handling |
| `ViewModels/KitRowViewModel.cs` | One kit row: state, category, AUX summary, pad details, plan text |
| `ViewModels/KitGroupViewModel.cs` | Group checkbox (checked / unchecked / mixed) |
| `ViewModels/AuxRoleViewModel.cs` | One AUX role dropdown |
| `MainWindow.xaml` | The only window; code-behind just triggers Connect on load |

### 7.3 Data on disk (`data/` next to the exe)

- `data/kits/kitNNN.json`: deep scan of one kit: `{ kit, scannedAt, blocks: { name: hex } }`. Same format as the
  Python `scanall.py` output.
- `data/index.json`: per kit the last seen header (hex of the common block) and category. Lets startup skip the
  category probe for unchanged kits.
- `data/backups/<yyyyMMdd-HHmmss>/kitNNN.json`: full kit snapshot taken right before an apply.

### 7.4 Workflows

**Startup / Refresh.** Connect (Identity). Read the header of all 200 kits. Per kit:
- deep scan exists and header equals its common block: state UpToDate (green check), category from the deep scan;
- otherwise state Changed (orange) or NotScanned (grey); category from `index.json` if the header is unchanged,
  else from the 3-block probe.

Only header changes are detected. Sound tweaks that leave the header alone keep the green check. This is accepted
because apply always re-reads kits live.

**Deep scan checked.** Reads all 148 blocks of each checked kit, saves the snapshot, updates the row.

**Apply to checked kits.** After a confirmation dialog, per kit:
1. Deep read the kit live (about 8 s).
2. Save it as a backup in a new session folder.
3. Plan the changes with `RolePlanner` on the fresh snapshot; only blocks that differ are written.
4. `WriteKitAsync`: write each block, read it back, compare with `Matches`. Layer blocks must keep bytes 0x00-0x08
   (switch, instrument, bank) and V-EDIT 2 (0x35-0x38) exactly; other differences are accepted as module
   adjustments. Non-layer blocks must match exactly.
5. On any failure, blocks already written are written back from the backup, the kit is marked red with the
   message (tooltip) and the run continues with the next kit. Writing a kit is not cancellable, so cancel only
   takes effect between kits. A kit is never left half written.
6. The snapshot is updated with what the module actually stored and saved as the new cache.

**Undo last apply.** Takes the newest backup session, reads the 36 AUX related blocks of each kit
(uc21-28, A/B/C21-28, pad11-14), writes back those that differ, updates the cache, then deletes the session if
everything succeeded. Repeating it walks back through older sessions.

### 7.5 Role rules (`RolePlanner`)

A role copies the source pad onto the AUX pad: head and rim KitUnitCommon, layers A/B/C of head and rim, and the
KitPad block (at most 9 blocks).

| Role | Source |
|---|---|
| Snare, Tom 1-4, Crash 1/2, Ride | The matching pad (ride head and edge go to AUX head and rim) |
| China, Splash | The kit's own pad with that type (target itself first, then Crash 1, Crash 2, Ride, AUX1-4, hi-hat, snare, toms). If none: Crash 1 settings with `Mn Bz Trad Ch`/`ChE` or `Mn Bz Dual Sp`/`SpE` on layer A, layers B and C off |
| Closed hi-hat | Hi-hat pad, V-EDIT 2 set to 2 on every hi-hat layer |
| Keep | Nothing |

Plans are computed on the fresh snapshot of each kit, so an AUX pad that is being overwritten can still serve as
the source for another AUX pad in the same apply (it uses the old content).

Known cosmetic issue: because the module stores V-EDIT 1 = 24 instead of 26 for some closed hi-hats, the preview
of an already converted kit still says "AUX1 <- closed hi-hat" instead of "already". Re-applying is harmless.

### 7.6 Build and publish

```
dotnet build src/KitRoles
dotnet publish src/KitRoles -c Release -o publish
```

Release builds are self-contained single-file `win-x64` (~65 MB). A post-publish target deletes the macOS native
library that DryWetMIDI ships as content.

Releases: `.github/workflows/release.yml` runs when a tag `v*` is pushed (`git tag v0.2.0`,
`git push origin v0.2.0`). It publishes with `-p:Version=<tag without v>` on a Windows runner and creates a GitHub
Release with `KitRoles.exe` attached and generated notes. The README links to
`releases/latest/download/KitRoles.exe`, so the asset name must stay `KitRoles.exe`.

## 8. Python tools (`tools/`)

Written for investigation; they work standalone with plain Python 3 (ctypes WinMM, no packages).

| Script | Purpose |
|---|---|
| `v51midi.py` | Minimal WinMM SysEx access, read-only by construction (refuses anything but RQ1/Identity) |
| `kitmap.py` | Same address map as the app |
| `readkit.py` | Read one kit (`python readkit.py current` or a number) and print its instruments |
| `scanall.py` | Read all 200 kits into `scratch/kits/` |
| `decode.py` | Layer decoder and firmware name lookup for analysis |
| `write_test.py` | The single-block write/verify/restore test used to confirm writing |
| `export_instruments.py` | Generates `instruments.json` from the firmware image |
| `extract_samples.py` | Extracts user samples from a `.TD0` module backup |

Paths in the scripts assume they are run from `tools/` and write to `../scratch/`.

## 9. Other data sources looked at

- **Module backup `.TD0`** (SD card): chunk table at the start (u16 end offset; 16-byte entries
  `tag[4] u32 u32 offset u32 length`). `KITa` = 201 kit records of 14508 bytes, `VKIa` = kit images (BMP),
  `VXPa` = 6 slot entries, `USPa`/`VUDa` = user samples. Not used by the app: the user changes kits often, so live
  reads are the source of truth. The `KITa` record layout was not decoded.
- **Roland Cloud Manager** (`%APPDATA%\Roland Cloud\Cloud Manager\main.db`, SQLite): catalog of products with
  EXV codes, no instrument lists. Expansion files were not downloaded to the PC.

## 10. Open questions and next steps

1. Do SysEx writes survive a power cycle? Apply to one kit, power cycle, Refresh/deep scan that kit.
2. Does the module send DT1 by itself when editing on the unit ("Transmit Edit Data")? Would allow live cache sync.
3. Meaning of hi-hat V-EDIT 2 = 1 (and other values), and whether a role for open hi-hat is wanted.
4. Names for downloaded expansion and kit pack instruments (see 6.4).
5. Does moving a pack to another slot keep its bank number?
6. Make the preview treat module-adjusted values as "already applied" (see 7.5).
7. Configurable default China/splash, possibly more roles.
8. First GitHub release with the published exe.
