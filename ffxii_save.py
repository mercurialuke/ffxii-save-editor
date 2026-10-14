"""FINAL FANTASY XII THE ZODIAC AGE (PC) save file codec.

Format (reverse-engineered from FFXII_TZA.exe):
    0x00  "SAFH"
    0x04  u32 uncompressed size
    0x08  u32 key rotation r (rand() % 32)
    0x0C  RC4(zlib(save data))

RC4 key = MD5 of the string "FINAL FANTASY XII THE ZODIAC AGE" rotated left by r
characters, truncated to its first 31 characters (CryptoAPI: CryptCreateHash(MD5)
-> CryptDeriveKey(CALG_RC4, 128-bit)).
"""
import hashlib
import struct
import zlib

MAGIC = b"SAFH"
TITLE = b"FINAL FANTASY XII THE ZODIAC AGE"


def rc4(key, data):
    s = list(range(256))
    j = 0
    for i in range(256):
        j = (j + s[i] + key[i % len(key)]) & 0xFF
        s[i], s[j] = s[j], s[i]
    out = bytearray(len(data))
    i = j = 0
    for n, b in enumerate(data):
        i = (i + 1) & 0xFF
        j = (j + s[i]) & 0xFF
        s[i], s[j] = s[j], s[i]
        out[n] = b ^ s[(s[i] + s[j]) & 0xFF]
    return bytes(out)


def key_for(r):
    rotated = TITLE[r:] + TITLE[:r]
    return hashlib.md5(rotated[:31]).digest()


def decode(blob):
    if blob[:4] != MAGIC:
        raise ValueError("not an FFXII save (missing SAFH)")
    size, r = struct.unpack_from("<II", blob, 4)
    plain = zlib.decompress(rc4(key_for(r), blob[12:]))
    if len(plain) != size:
        raise ValueError("size mismatch: header %d, data %d" % (size, len(plain)))
    return plain, r


def encode(plain, r=0):
    body = rc4(key_for(r), zlib.compress(plain, 9))
    return MAGIC + struct.pack("<II", len(plain), r) + body


if __name__ == "__main__":
    import sys

    if len(sys.argv) != 4 or sys.argv[1] not in ("decode", "encode"):
        sys.exit("usage: ffxii_save.py decode <save> <out.raw>\n"
                 "       ffxii_save.py encode <in.raw> <save>   (does not fix the inner CRC)")
    data = open(sys.argv[2], "rb").read()
    if sys.argv[1] == "decode":
        plain, r = decode(data)
        open(sys.argv[3], "wb").write(plain)
        print("decoded %d bytes (key rotation %d)" % (len(plain), r))
    else:
        open(sys.argv[3], "wb").write(encode(data))
        print("encoded %d bytes" % len(data))
