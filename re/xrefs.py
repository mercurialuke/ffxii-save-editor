# Find instructions that reference a given import or address (call/jmp/lea/mov via RIP).
# usage: xrefs.py <import-name-substring | 0xADDR> ...
import sys

from capstone import CS_ARCH_X86, CS_MODE_64, Cs
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

sys.path.insert(0, __file__.rsplit("\\", 1)[0])
import disx  # noqa: E402

targets = {}
for q in sys.argv[1:]:
    if q.startswith("0x"):
        targets[int(q, 16)] = q
    else:
        for a, n in disx.imports.items():
            if q.lower() in n.lower():
                targets[a] = n

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
        t = None
        if op.type == X86_OP_MEM and op.mem.base == 41:
            t = ins.address + ins.size + op.mem.disp
        elif op.type == X86_OP_IMM and ins.mnemonic in ("call", "jmp"):
            t = op.imm
        if t in targets:
            f = disx.func_of(ins.address)
            print("%x %-6s %-40s func %s" % (ins.address, ins.mnemonic, targets[t], "%x" % f[0] if f else "?"))
