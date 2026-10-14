# Find instructions whose memory displacement or immediate falls in a range.
# usage: find_disp.py <lo> <hi>
import sys

from capstone import CS_ARCH_X86, CS_MODE_64, Cs
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

sys.path.insert(0, __file__.rsplit("\\", 1)[0])
import disx  # noqa: E402

lo, hi = int(sys.argv[1], 0), int(sys.argv[2], 0)
text = [s for s in disx.pe.sections if s.Name.startswith(b".text")][0]
start = disx.BASE + text.VirtualAddress
code = disx.read(start, text.Misc_VirtualSize)
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True
md.skipdata = True
for ins in md.disasm(code, start):
    if ins.id == 0:
        continue
    for op in ins.operands:
        v = None
        if op.type == X86_OP_MEM and op.mem.base not in (0, 41):
            v = op.mem.disp
        elif op.type == X86_OP_IMM and ins.mnemonic in ("add", "lea", "mov", "sub", "cmp"):
            v = op.imm
        if v is not None and lo <= v <= hi:
            f = disx.func_of(ins.address)
            print("%x  %-6s %-40s func %s" % (ins.address, ins.mnemonic, ins.op_str, "%x" % f[0] if f else "?"))
            break
