# RolandKitRoles

A small Windows tool for the Roland V51 drum module (TD-516 and other V51 based kits). It lets you decide once what
your extra AUX pads should play, and applies that to as many kits as you like in one go.

## The problem

The V51 has 200 kits, and every kit stores its own sound for every pad, including AUX1 to AUX4. There is no global
setting along the lines of "AUX2 is a China in every kit".

If you add extra cymbals or pads to the AUX inputs, every kit decides on its own what they play. One kit has a
splash on AUX1, the next one a stacked cymbal, a downloaded kit pack puts a song specific sound there, and a blank
user kit plays whatever the template had. When you are learning a song and want AUX2 to be a China this week and a
closed hi-hat next week, you have to walk through the kit editor of every single kit you want to try. With a
handful of Roland Cloud expansions installed, that is dozens of kits.

## What it does

- Connects to the V51 over USB and reads every kit: names, and what each pad and layer plays.
- Shows real instrument names for the built-in sounds and the preinstalled expansions, instead of bare numbers.
- Sorts the kits into groups: built-in kits, instrument expansion kits, kit packs and blank user kits.
- Lets you pick a role for each AUX pad, for example:
  - AUX1 = Tom 1
  - AUX2 = China
  - AUX3 = Closed hi-hat
  - AUX4 = Keep (leave it alone)
- Shows a preview per kit of what will change before anything is written.
- Writes the roles to the checked kits, verifies every change by reading it back, and keeps a backup so you can undo.

## How a role is chosen per kit

The tool copies a pad that already exists in the same kit, so the AUX pad sounds like it belongs to that kit,
with the same tuning, volume and effect settings the kit's author picked.

- Tom, snare, crash and ride roles copy the matching pad of the kit.
- China and splash use the kit's own China or splash if it has one on any pad. If it does not, a built-in China or
  splash is used with the kit's Crash 1 settings.
- Closed hi-hat copies the kit's hi-hat and fixes it in the closed position, so it plays closed no matter where the
  hi-hat pedal is.

## Typical uses

- Learning songs: switch AUX2 between China, splash and closed hi-hat across all your kits in a minute.
- Trying out a new expansion: install it, refresh, and give its kits the same AUX layout as the rest.
- Keeping a consistent setup: every kit gets the same AUX layout, so you do not have to remember what each kit does.

## Requirements

- Windows 10 or 11, 64-bit.
- A Roland V51 module connected via USB, with the Roland V51 driver installed.
- Tested with V51 system program 2.10.

## Getting started

1. Back up your module to an SD card first, using the V51 backup function. The tool keeps its own backups, but a full
   module backup is the safest starting point.
2. Download [the latest KitRoles.exe](https://github.com/Dandiee/RolandKitRoles/releases/latest/download/KitRoles.exe) (all versions are on the [releases page](https://github.com/Dandiee/RolandKitRoles/releases)) and run it.
   No installation needed. It stores its data in a `data` folder next to the exe, so keep them together.
3. The tool connects, reads all 200 kits and shows what every pad plays. The first start takes about two minutes;
   after that it only rescans kits whose name or main settings changed, so starting takes about 15 seconds.
4. Check the kits you want to change. The group checkboxes (built-in, expansion, kit packs, user kits) help.
5. Pick the AUX roles, check the preview column, and press **Apply to checked kits**.
6. If you do not like the result, **Undo last apply** restores the AUX pads. Pressing it again goes back one more
   apply.

## Safety

- Scanning only reads from the module.
- Before writing, each kit is read again from the module, and the pad settings about to change are saved as a
  backup under `data\backups`.
- Only the AUX pad settings that actually change are written. Every written block is read back and checked.
- If a check fails, the kit's AUX pads are restored from that backup and the kit is marked with an error.

## Limitations

- Instrument names for downloaded Roland Cloud expansions and kit packs are not available to the tool, so those
  sounds show up as "Expansion 2012 #4" and similar. Roles still work for them.
- Only the four AUX pads can be given a role. The main pads are never changed.
- Sound changes you make on the module that do not touch the kit's name or main settings are not detected
  automatically. Use **Rescan checked** for those kits. Applying always reads the kit fresh before writing.

## Building from source

Requires the .NET 10 SDK.

```
dotnet build src/KitRoles
dotnet publish src/KitRoles -c Release -o publish
```

The publish step produces a single self-contained `KitRoles.exe`.

## License

PolyForm Noncommercial 1.0.0. You may use, modify and share this software for any noncommercial purpose, as long as
the license and the copyright notice stay with it. Commercial use is not permitted. See [LICENSE](LICENSE).

## Disclaimer

This is an independent hobby project. It is not affiliated with, endorsed by, or supported by Roland Corporation.
Roland and V-Drums are trademarks of Roland Corporation. Instrument names shown by the tool are read from the
module's own system program. Use at your own risk; keep a backup of your module.
