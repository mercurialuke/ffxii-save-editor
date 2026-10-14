# Find instructions that store a given immediate into memory (rip-relative or [reg+disp]).
# usage: find_imm.py <imm> [width]
import sys
from collections import defaultdict

from capstone import CS_ARCH_X86, CS_MODE_64, Cs
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

sys.path.insert(0, __file__.rsplit("\\", 1)[0])
import disx  # noqa: E402

imm = int(sys.argv[1], 0)
width = int(sys.argv[2]) if len(sys.argv) > 2 else 2
text = [s for s in disx.pe.sections if s.Name.startswith(b".text")][0]
start = disx.BASE + text.VirtualAddress
code = disx.read(start, text.Misc_VirtualSize)
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True
md.skipdata = True
rip_hits = []
reg_hits = defaultdict(list)
for ins in md.disasm(code, start):
    if ins.id == 0 or ins.mnemonic != "mov" or len(ins.operands) != 2:
        continue
    dst, src = ins.operands
    if dst.type != X86_OP_MEM or src.type != X86_OP_IMM or src.imm != imm or dst.size != width:
        continue
    if dst.mem.base == 41:
        rip_hits.append((ins.address + ins.size + dst.mem.disp, ins.address))
    else:
        reg_hits[ins.address >> 6].append((ins.address, ins.op_str))
rip_hits.sort()
print("rip-relative stores of %#x (width %d): %d" % (imm, width, len(rip_hits)))
for i, (t, a) in enumerate(rip_hits):
    near = [x for x in rip_hits if 0 < abs(x[0] - t) <= 8]
    if near:
        print("  target %x  at %x" % (t, a))
# clusters of [reg+disp] stores close together in code
print("register-based clusters (>=3 stores within 64 bytes of code):")
flat = sorted((a, s) for v in reg_hits.values() for a, s in v)
for i in range(len(flat)):
    grp = [x for x in flat if 0 <= x[0] - flat[i][0] < 64]
    if len(grp) >= 3 and (i == 0 or flat[i][0] - flat[i - 1][0] >= 64 or len([x for x in flat if 0 <= x[0] - flat[i - 1][0] < 64]) < 3):
        print("  ", ", ".join("%x: %s" % g for g in grp))
