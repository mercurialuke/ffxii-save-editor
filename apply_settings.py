"""Change in-save settings of FFXII: The Zodiac Age (PC) saves in place.

    python apply_settings.py FFXII_001 --subtitles on --music 50
    python apply_settings.py FFXII_000 FFXII_001 --voice 80 --sfx 100

Save names are looked up in the game's save folder unless a path is given.
Back up your saves first (see README). Only the settings block and the
checksum are changed; this is verified before writing.
"""
import argparse
import glob
import os
import struct
import zlib

import ffxii_save as fs

SAVE_ROOT = os.path.join(os.path.expanduser("~"), "Documents", "My Games", "FINAL FANTASY XII THE ZODIAC AGE")

S = 0x8208                  # settings block in the decoded save
SUBTITLES_OFF = 1 << 12     # flags bit: 1 = subtitles off
MUSIC, SFX, VOICE = S + 8, S + 0xA, S + 0xC
CRC_START, CRC_END = 8, 0xE200


def find(name):
    if os.path.exists(name):
        return name
    hits = glob.glob(os.path.join(SAVE_ROOT, "*", name))
    if len(hits) != 1:
        raise SystemExit("can't find save %r (found %d matches)" % (name, len(hits)))
    return hits[0]


def fix_crc(d):
    c = zlib.crc32(bytes(d[CRC_START:CRC_END])) & 0xFFFFFFFF
    struct.pack_into("<I", d, 0, c)
    struct.pack_into("<I", d, CRC_END, c)


def describe(d):
    flags = struct.unpack_from("<I", d, S)[0]
    m, s, v = struct.unpack_from("<3H", d, MUSIC)
    return "subtitles %s, music %d, sfx %d, voice %d" % ("off" if flags & SUBTITLES_OFF else "on", m, s, v)


def patch(path, args):
    plain, r = fs.decode(open(path, "rb").read())
    d = bytearray(plain)
    before = describe(d)
    flags = struct.unpack_from("<I", d, S)[0]
    if args.subtitles is not None:
        flags = flags & ~SUBTITLES_OFF if args.subtitles == "on" else flags | SUBTITLES_OFF
        struct.pack_into("<I", d, S, flags)
    for value, off in ((args.music, MUSIC), (args.sfx, SFX), (args.voice, VOICE)):
        if value is not None:
            struct.pack_into("<H", d, off, max(0, min(100, value)))
    fix_crc(d)

    enc = fs.encode(bytes(d), r)
    back, _ = fs.decode(enc)
    assert back == bytes(d)
    changed = [i for i in range(len(plain)) if plain[i] != back[i]]
    assert all(i < 4 or S <= i < S + 16 or CRC_END <= i < CRC_END + 4 for i in changed), changed[:10]
    if not args.dry_run:
        open(path, "wb").write(enc)
    print("%s: %s -> %s%s" % (os.path.basename(path), before, describe(back), " (dry run)" if args.dry_run else ""))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("saves", nargs="+", help="save file names (FFXII_000, ...) or paths")
    ap.add_argument("--subtitles", choices=["on", "off"])
    ap.add_argument("--music", type=int, help="music volume 0-100")
    ap.add_argument("--sfx", type=int, help="sound effects volume 0-100")
    ap.add_argument("--voice", type=int, help="voice volume 0-100")
    ap.add_argument("--dry-run", action="store_true", help="show the change without writing")
    args = ap.parse_args()
    for name in args.saves:
        patch(find(name), args)


if __name__ == "__main__":
    main()
