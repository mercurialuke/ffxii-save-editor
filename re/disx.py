# Disassemble the function containing a VA (or a range), annotating imports and strings.
# usage: dis.py <va hex> [maxinsns]      dis.py --range <start> <end>
import bisect
import re
import struct
import sys

import pefile
from capstone import CS_ARCH_X86, CS_MODE_64, Cs
from capstone.x86 import X86_OP_MEM

from gamepath import EXE  # noqa: E402
pe = pefile.PE(EXE, fast_load=True)
pe.parse_data_directories(directories=[pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_IMPORT"],
                                       pefile.DIRECTORY_ENTRY["IMAGE_DIRECTORY_ENTRY_EXCEPTION"]])
BASE = pe.OPTIONAL_HEADER.ImageBase
img = pe.get_memory_mapped_image()

imports = {}
for entry in pe.DIRECTORY_ENTRY_IMPORT:
    for imp in entry.imports:
        if imp.name:
            imports[imp.address] = "%s!%s" % (entry.dll.decode(), imp.name.decode())

funcs = sorted((BASE + rf.struct.BeginAddress, BASE + rf.struct.EndAddress) for rf in pe.DIRECTORY_ENTRY_EXCEPTION)
starts = [f[0] for f in funcs]


def func_of(va):
    i = bisect.bisect_right(starts, va) - 1
    if i >= 0 and funcs[i][0] <= va < funcs[i][1]:
        return funcs[i]
    return None


def read(va, n):
    o = va - BASE
    return img[o:o + n] if 0 <= o < len(img) else b""


def describe(va):
    if va in imports:
        return imports[va]
    s = read(va, 80)
    m = re.match(rb"[\x20-\x7e]{4,}", s)
    if m:
        return '"%s"' % m.group().decode()
    try:
        w = s[:80].decode("utf-16-le")
        m2 = re.match(r"[\x20-\x7e]{4,}", w)
        if m2:
            return 'L"%s"' % m2.group()
    except Exception:
        pass
    return None


md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True


def dis(start, end, limit=100000):
    code = read(start, end - start)
    n = 0
    for ins in md.disasm(code, start):
        note = ""
        for op in ins.operands:
            if op.type == X86_OP_MEM and op.mem.base == 41:  # RIP
                target = ins.address + ins.size + op.mem.disp
                d = describe(target)
                note = "  ; [%x] %s" % (target, d or "")
                if ins.mnemonic == "call" and target in imports:
                    note = "  ; call " + imports[target]
        if ins.mnemonic == "call" and ins.op_str.startswith("0x"):
            t = int(ins.op_str, 16)
            note = "  ; -> %x" % t
        print("%x: %-8s %s%s" % (ins.address, ins.mnemonic, ins.op_str, note))
        n += 1
        if n >= limit:
            break


if __name__ == "__main__":
    if sys.argv[1] == "--range":
        dis(int(sys.argv[2], 16), int(sys.argv[3], 16))
    else:
        va = int(sys.argv[1], 16)
        f = func_of(va)
        print("function %x-%x" % f if f else "no function")
        dis(f[0], f[1], int(sys.argv[2]) if len(sys.argv) > 2 else 100000)
