# List rip-relative references into address ranges: xrange.py lo-hi [lo-hi ...]
import sys

from capstone import CS_ARCH_X86, CS_MODE_64, Cs
from capstone.x86 import X86_OP_MEM

sys.path.insert(0, __file__.rsplit("\\", 1)[0])
import disx  # noqa: E402

ranges = [tuple(int(x, 16) for x in r.split("-")) for r in sys.argv[1:]]
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
        if op.type == X86_OP_MEM and op.mem.base == 41:
            t = ins.address + ins.size + op.mem.disp
            for lo, hi in ranges:
                if lo <= t < hi:
                    f = disx.func_of(ins.address)
                    print("%x  target %x (+%#x)  %-6s %-45s func %s" % (ins.address, t, t - lo, ins.mnemonic, ins.op_str, "%x" % f[0] if f else "?"))
