# For each function, pair rip-relative base addresses (lea/mov of an address) with
# [reg + disp] accesses, and report combinations that land on given target addresses.
# usage: fieldrefs.py <hex target> ...    (targets are absolute addresses)
import sys
from collections import defaultdict

from capstone import CS_ARCH_X86, CS_MODE_64, Cs
from capstone.x86 import X86_OP_MEM

sys.path.insert(0, __file__.rsplit("\\", 1)[0])
import disx  # noqa: E402

targets = [int(t, 16) for t in sys.argv[1:]]
lo, hi = min(targets) - 0x200000, max(targets) + 1
text = [s for s in disx.pe.sections if s.Name.startswith(b".text")][0]
start = disx.BASE + text.VirtualAddress
code = disx.read(start, text.Misc_VirtualSize)
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True
md.skipdata = True

bases = defaultdict(set)   # func -> set of base addresses loaded
disps = defaultdict(list)  # func -> list of (addr, disp, size, text)
for ins in md.disasm(code, start):
    if ins.id == 0:
        continue
    f = disx.func_of(ins.address)
    fk = f[0] if f else ins.address >> 12
    for op in ins.operands:
        if op.type != X86_OP_MEM:
            continue
        if op.mem.base == 41:
            t = ins.address + ins.size + op.mem.disp
            if lo <= t < hi and ins.mnemonic in ("lea", "mov"):
                bases[fk].add(t)
        elif op.mem.disp:
            disps[fk].append((ins.address, op.mem.disp, op.size, "%s %s" % (ins.mnemonic, ins.op_str)))

for fk, bs in bases.items():
    for a, d, size, txt in disps.get(fk, []):
        for b in bs:
            if b + d in targets:
                print("func %x  base %x + %#x = %x  (%d bytes)  %x: %s" % (fk, b, d, b + d, size, a, txt))
