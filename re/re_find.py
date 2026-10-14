# Locate SAFH handling and compression/crypto hints in FFXII_TZA.exe.
import re
import struct
import sys

import pefile

from gamepath import EXE  # noqa: E402
pe = pefile.PE(EXE, fast_load=True)
data = open(EXE, "rb").read()
base = pe.OPTIONAL_HEADER.ImageBase
print("image base %x" % base)
for s in pe.sections:
    print("section %-8s va %08x size %08x raw %08x" % (s.Name.rstrip(b"\0").decode(), s.VirtualAddress, s.Misc_VirtualSize, s.PointerToRawData))


def off_to_va(off):
    for s in pe.sections:
        if s.PointerToRawData <= off < s.PointerToRawData + s.SizeOfRawData:
            return base + s.VirtualAddress + off - s.PointerToRawData
    return None


# 1. immediate 'SAFH' (0x48464153) anywhere in the file
needle = struct.pack("<I", 0x48464153)
for m in re.finditer(re.escape(needle), data):
    va = off_to_va(m.start())
    print("SAFH bytes at file %x va %s context %s" % (m.start(), hex(va) if va else None, data[m.start() - 8:m.start() + 8].hex()))

# 2. compression / crypto library fingerprints
for pat in [rb"inflate [0-9.]+", rb"deflate [0-9.]+", rb"zlib", rb"lz4", rb"LZ4", rb"zstd", rb"Zstandard", rb"LZMA", rb"Mark Adler", rb"Jean-loup",
            rb"AES", rb"Rijndael", rb"blowfish", rb"Blowfish", rb"CryptAcquireContext", rb"BCrypt\w+", rb"MD5", rb"SHA1", rb"crc32", rb"CRC32", rb"\.sav", rb"FFXII_%03d", rb"FFXII_", rb"SaveData", rb"savedata"]:
    hits = [(m.start(), m.group()) for m in re.finditer(pat, data)][:6]
    if hits:
        print(pat.decode(errors="replace"), [(hex(o), g[:40]) for o, g in hits])

# 3. the AES S-box / zlib tables as data fingerprints
sbox = bytes.fromhex("637c777bf26b6fc53001672bfed7ab76")
for m in re.finditer(re.escape(sbox), data):
    print("AES sbox at file %x" % m.start())
crc_tab = struct.pack("<IIII", 0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA)
for m in re.finditer(re.escape(crc_tab), data):
    print("CRC32 table at file %x va %x" % (m.start(), off_to_va(m.start())))
# zlib length base table (inflate lbase) as u16
lbase = struct.pack("<8H", 3, 4, 5, 6, 7, 8, 9, 10)
for m in re.finditer(re.escape(lbase), data):
    print("inflate lbase table at file %x va %x" % (m.start(), off_to_va(m.start())))
