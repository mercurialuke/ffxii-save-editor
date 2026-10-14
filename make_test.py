"""Copy a save into a new slot with marker values, to identify settings in-game.

    python make_test.py FFXII_001 2      # writes FFXII_002 (slot 3)

Sets the three volume slots to 10/50/90 and clears the subtitles-off bit, so
the in-game Config screen shows which field is which. Never overwrites a slot.
"""
import os
import struct
import sys
import zlib

import ffxii_save as fs
from apply_settings import CRC_END, CRC_START, S, SUBTITLES_OFF, find, fix_crc


def main():
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    src = find(sys.argv[1])
    dst_slot = int(sys.argv[2])
    plain, r = fs.decode(open(src, "rb").read())
    d = bytearray(plain)

    flags = struct.unpack_from("<I", d, S)[0]
    print("before: flags=%#x  vols=%s  music track=%d" % (
        flags, struct.unpack_from("<3H", d, S + 8), struct.unpack_from("<H", d, S + 0xE)[0]))
    struct.pack_into("<I", d, S, flags & ~SUBTITLES_OFF)
    struct.pack_into("<3H", d, S + 8, 10, 50, 90)
    struct.pack_into("<I", d, 0x50, struct.unpack_from("<I", d, 0x50)[0] + 1)  # save counter
    d[0x54] = dst_slot                                                          # slot number
    fix_crc(d)

    out = os.path.join(os.path.dirname(src), "FFXII_%03d" % dst_slot)
    if os.path.exists(out):
        raise SystemExit("refusing to overwrite existing " + out)
    enc = fs.encode(bytes(d), r)
    check, _ = fs.decode(enc)
    assert check == bytes(d)
    assert zlib.crc32(check[CRC_START:CRC_END]) & 0xFFFFFFFF == struct.unpack_from("<I", check, 0)[0]
    open(out, "wb").write(enc)
    print("wrote", out, len(enc), "bytes")


if __name__ == "__main__":
    main()
