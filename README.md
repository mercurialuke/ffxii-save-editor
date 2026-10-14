# FFXII: The Zodiac Age (PC) save editor

A save editor for *FINAL FANTASY XII THE ZODIAC AGE* (Steam), plus the reverse-engineering notes behind it. As far as I know this is the first public documentation of the PC save format.

> **Made with AI.** This project was developed with an AI coding assistant (Anthropic's Claude, via Claude Code). The AI did the reverse engineering and wrote the code and the documentation. I directed the work and tested the results on my own saves in-game, but I didn't write the code by hand and don't know every line of it. Keep backups (the editor makes them automatically), and please report problems in the issues.

## The editor (Windows app)

`editor\FFXIISaveEditor.exe` finds your saves in `Documents\My Games\FINAL FANTASY XII THE ZODIAC AGE\<steam id>\` and edits them in place. Build it with `editor\build.bat`; it needs only the .NET Framework 4.x that ships with Windows.

What it edits:

- **General.** Gil and clan points. The options stored in each save: subtitles, music/SFX/voice volume, music track (original, reorchestrated or OST), battle mode and Japanese voices. Also the confirm button (Type A/B), which lives in `GameSetting.ini`.
- **Characters** (all 17 records, including guests):
  - Level, EXP, LP, HP/MP and base Strength/Magick/Vitality/Speed.
  - Both jobs and all five equipment slots.
  - All 12 gambit slots (target, action, on/off).
  - All 361 licenses.
- **Inventory.** Quantities of items, weapons, armor, accessories, ammo and loot.
- **Magicks, technicks, gambits and key items.** Owned or not.

How it keeps saves safe:

- **Backups.** Every write first copies the original to `%LOCALAPPDATA%\FFXII Save Editor\Backups\<timestamp>\`; the *Backups* button opens that folder.
- **Verification.** Before writing, the editor re-encodes the save, decodes it again and compares. After writing, it reads the file back from disk.
- **Bookkeeping.** It keeps the save's own records consistent: the ordered inventory lists and their counts, the ownership bitfields, the "worn" counts of equipment, the second copy of the character and inventory data, the load-screen summary and the checksum. When it opens a save, it reports anything inconsistent that was already in the file.
- **Save as slot….** Writes the edited save to another slot (for example an empty one) and leaves the original untouched. Use it to try changes before you commit them to your main save.

Caveats:

- **Derived values.** Raising the level doesn't roll the HP and stat gains of real level-ups. Set those yourself if you want them.
- **Licenses.** Learning a license sets its bit only. It doesn't spend LP.
- **Equipment.** The editor doesn't check licenses or two-handed weapons.
- **Key items** drive quests and story events. Adding or removing them can break a quest.
- **Game running.** Edit with the game closed, or at least before you open the Load menu. Saving in-game to the same slot afterwards overwrites the edit.

### Command-line tools

The Python tools predate the app and are still handy for scripting.

```
python apply_settings.py FFXII_001 --subtitles on --music 50
python apply_settings.py FFXII_000 FFXII_001 --voice 80 --dry-run
python ffxii_save.py decode FFXII_001 slot2.raw     # inspect the decrypted data
```

`apply_settings.py` verifies that only the settings block and the checksum changed before it writes anything.

### Confirm button (A vs B)

The confirm button is *not* stored in the save. It's `DecideButton` in `GameSetting.ini` (same folder):

| `DecideButton=` | In-game "Confirm Button" | Xbox controller |
|---|---|---|
| `Cross` | Type A | A confirms |
| `Circle` | Type B | B confirms |

Change it with the game closed, because the game writes this file back.

## Save file format

```
0x00  "SAFH"
0x04  u32  decompressed size (353,936 for every save seen)
0x08  u32  r = rand() % 32
0x0C  RC4( zlib(level 9)( data ) )
```

- **Encryption.** The RC4 key comes from Windows CryptoAPI, via provider `"Microsoft Enhanced Cryptographic Provider v1.0"` and container `RSA_SQEX_FFXII_TZA`. `CryptCreateHash(CALG_MD5)` hashes the string `"FINAL FANTASY XII THE ZODIAC AGE"` rotated left by `r` characters and cut to 31 characters. `CryptDeriveKey(CALG_RC4, 128-bit)` turns that hash into the key, so the key is just the MD5 digest. The game encrypts in 1000-byte `CryptEncrypt` calls, but RC4 is a stream cipher, so that's the same as encrypting everything at once.
- **Decrypted data.** This is a memory image starting at game address `0x2164280` (the exe loads at its preferred base `0x120000`). A Cheat Engine address `FFXII_TZA.exe+X` is save offset `X + 0x120000 − 0x2164280`.
  - `0x0000–0xE200` is the PS2-era save block. `0x00` holds a CRC32 of `[0x8, 0xE200)`, also stored at `0xE200`.
  - The character records and the quantity/ownership area also exist a second time, `0x4C48C` bytes further on. The game writes both copies, and so does the editor.

All offsets below are in the decoded data (first copy).

### Header

| Offset | Type | Meaning |
|---|---|---|
| 0x08 | u32 | Gil |
| 0x10 | u32 | Probably play time (the same value is at `0xC9EC`), unit not confirmed |
| 0x20 | 5 × (u8, u8, u16) | Load-screen party: character index, level (`0xFF` = empty) |
| 0x40 | u32 | Copy of the clan points |
| 0x50 | u32 | Save counter |
| 0x54 | u8 | Slot number (file `FFXII_00n` = slot n) |

### Settings block (`0x8208`)

In game memory this block is reached through the pointer at `0x215E6E0`.

| Offset | Type | Meaning |
|---|---|---|
| +0x00 | u32 flags | bit 3 Battle Mode (0 Wait, 1 Active) · bits 4–11 Battle Speed · **bit 12 subtitles off** · bit 13 camera Y inverted · bit 14 camera X inverted · bit 15 vibration |
| +0x04 | u32 flags | bit 10 Japanese voices · bits 8, 9, 21, 22, 23–26: other options (screen shake, target lines, speed mode…), not individually confirmed |
| +0x08 | u16 | Music volume 0–100 (audio channel 1) |
| +0x0A | u16 | Sound effects volume 0–100 (channel 2) |
| +0x0C | u16 | Voice volume 0–100 (channel 3) |
| +0x0E | u16 | Music: 0 Original, 1 Reorchestrated, 2 OST |

Sources in `FFXII_TZA.exe`:
- **Defaults:** copied from `.rdata` `0x917268`, then adjusted per language at `0x2DDE60`.
- **Apply settings:** `0x2DDA30`.
- **Getters and setters:** `0x2DDC30`–`0x2DE340`.
- **Config menu load/save:** `0x2403F0` / `0x2407F0`, a switch on menu item number.

The Controls page (mouse sensitivity, camera auto-rotation, confirm button) comes from `GameSetting.ini`, not the save. The confirm button is offset `+0x64` of the struct whose pointer is at `0x1F82D20`.

### Characters (`0x2210 + i × 0x1C8`)

The order of `i`: Vaan, Ashe, Fran, Balthier, Basch, Penelo, Reks, Amalia, Basch (shirtless), Basch (dark suit), Lamont, Vossler (Imperial), Vossler (Resistance), Larsa, Reddas, Basch (Resistance), Reddas (duplicate).

| Offset | Type | Meaning |
|---|---|---|
| +0x08 | 4 × f32 | Base Strength, Magick Power, Vitality, Speed |
| +0x1C | u32 | Max HP |
| +0x20 | u16 | Max MP |
| +0x22 | 4 × u8 | Strength, Magick, Vitality, Speed totals (recomputed by the game) |
| +0x26 / +0x27 / +0x28 / +0x2D | u8 | Attack, Defense, Magick Resist, Evade (recomputed) |
| +0x40 | u32 | HP |
| +0x44 | u16 | MP |
| +0x46 | u16 | Mist charges |
| +0x48 | 5 × u16 | Equipment: weapon, shield/ammo, head, body, accessory (`0xFFFF` = none) |
| +0x52 | 5 × u16 | A second equipment set, only filled for some characters (not understood) |
| +0x7D | u8 | 1 = in the active party |
| +0x80 | 12 × (u16 target, u16 action) | Gambits (`0xFFFF` = empty slot) |
| +0xB0 | u16 | Gambit on/off bits |
| +0x184 | u32 | EXP |
| +0x188 | u32 | LP |
| +0x18C | 46 bytes | License bitfield, bit = license ID |
| +0x1BA | u8 | Level |
| +0x1BB / +0x1BC | u8 | First / second job (`0xFF` = none) |
| +0x1BD | u8 | Copy of the first job |

Jobs: 0 White Mage, 1 Uhlan, 2 Machinist, 3 Red Battlemage, 4 Knight, 5 Monk, 6 Time Battlemage, 7 Foebreaker, 8 Archer, 9 Black Mage, 10 Bushi, 11 Shikari.

Gambit targets are IDs `0x6000+`. Gambit actions use their own numbering:

| Action IDs | Meaning |
|---|---|
| 0–80 | Magicks |
| 82–145 | Items |
| 146 | Do nothing |
| 150 | Attack |
| 158–181 | Technicks |
| 182+ | Espers, Quickenings and so on |

### Inventory

Each category has an ordered list of u16 IDs that ends in `0xFFFF` (the in-game display order, with new entries appended at the end), plus a u32 count:

| Category | IDs | List | Capacity | Count | Stored as |
|---|---|---|---|---|---|
| Items | `0x0000–0x003F` | `0xBACC` | 64 | `0xC9B8` | u16 quantity at `0x6948 + 2·id` |
| Weapons | `0x1000–0x10C7` | `0xBB4C` | 200 | `0xC9BC` | equipment quantity |
| Armor | `0x10C8–0x1153` | `0xBCDC` | 140 | `0xC9C0` | equipment quantity |
| Accessories | `0x1154–0x1183` | `0xBDF4` | 48 | `0xC9C4` | equipment quantity |
| Ammo | `0x1184–0x11A3` | `0xBE54` | 32 | `0xC9C8` | equipment quantity |
| Gambits | `0x6000–0x60FF` | `0xBEE4` | 256 | `0xC9D0` | bit at `0x7120` |
| Technicks | `0x4000–0x4017` | `0xC0E4` | 24 | `0xC9D8` | bit at `0x711C` |
| Magicks | `0x3000–0x3050` | `0xC114` | 81 | `0xC9DC` | bit at `0x7110` |
| Key items | `0x8000+` | `0xC1B6` | 512 | `0xC9E0` | bit at `0x7140` |
| Loot | `0x2000–0x21FF` | `0xC5B6` | 512 | `0xC9E4` | u16 quantity at `0x6D10 + 2·(id − 0x2000)` |

Equipment quantities are u16 at `0x69C8 + 2·(id − 0x1000)`. The low 10 bits are the number owned, *including* worn copies. The top 6 bits count how many copies the active party (characters with `+0x7D = 1`) is wearing. Guest equipment (`0x11A4+`) can't be owned.

Clan points: u32 at `0xBA68`.

## Crash on startup or loading: NVIDIA shader cache

FFXII still truncates some pointers to 32 bits. It crashes (access violation at `FFXII_TZA.exe+0xB6E20` or `+0x1BF090`, reading `0xFFFFFFFF8xxxxxxx`) as soon as an allocation lands above the 2 GB line.

The NVIDIA driver maps the game's DirectX shader cache file into the process. When that file grows from 16 MB to 256 MB (`%LOCALAPPDATA%\NVIDIA\DXCache\<hash>.nvph`, starting with the magic `nvph`), the low 2 GB no longer fits and the game crashes every time. After a crash, the game also resets `GameSetting.ini` to exclusive fullscreen 2560×1440, which makes things worse.

**Fixes:**
- **Quick:** delete the game's large `.nvph` file. It will grow back over time.
- **Permanent:** NVIDIA Control Panel → Manage 3D settings → Program Settings → `FFXII_TZA.exe` → Shader Cache Size → **Disabled**.

## Layout

```
editor/              the Windows app (C#, build.bat), name tables, tests/
  tests/SelfTest.cs    model tests: SelfTest.exe <copy of a save> <output dir>
  tests/UiTest.cs      drives the real form: UiTest.exe <copy of a save> <backup dir>
ffxii_save.py        save codec (decode/encode)
apply_settings.py    edit in-save settings
make_test.py         write a copy of a save to a new slot with marker values (used to map settings)
tools/gen_tables.py  generates editor/Tables.cs from the ID lists credited below
tools/memtool.cs     scan/read/write another process's memory (build with csc from .NET Framework)
tools/secsize.cs     print the size of named shared-memory sections
tools/dxgi_adapters.cs  list DXGI adapters with their LUIDs
re/                  capstone/pefile/minidump scripts used for the reverse engineering
backups/ work/       local only (gitignored): save backups, decoded saves, built tools
```

The `re/` scripts need `pip install capstone pefile minidump`. They look for the game in Steam's default library; set `FFXII_EXE` to the full path of `FFXII_TZA.exe` if yours is elsewhere.

## Credits

- **Names** of items, equipment, loot, key items, magicks, technicks and licenses come from [ff12tza-utils](https://github.com/LuminaSapphira/ff12tza-utils) by LuminaSapphira (MIT licence; see `THIRD_PARTY_NOTICES.md`).
- **Gambit target and action names, and equipment types,** come from the data files of the PS2 *International Zodiac Job System* "rich" save editor. The PC version kept the IZJS IDs.
- **Cheat Engine table** from [niemasd/Game-Saves](https://github.com/niemasd/Game-Saves), used to cross-check offsets.

## Licence

MIT, see `LICENSE`. Name tables derived from ff12tza-utils keep their own MIT notice in `THIRD_PARTY_NOTICES.md`. FINAL FANTASY XII is a trademark of Square Enix; this project isn't affiliated with or endorsed by Square Enix.
