import ntpath
import sys
from collections import defaultdict

from minidump.minidumpfile import MinidumpFile

mf = MinidumpFile.parse(sys.argv[1])
LOW = 0x80000000

print("== modules (base, size, name) ==")
for m in sorted(mf.modules.modules, key=lambda m: m.baseaddress):
    flag = "  <-- below 2GB" if m.baseaddress < LOW else ""
    print("%016x %8d KB  %s%s" % (m.baseaddress, m.size // 1024, m.name, flag))

mi = getattr(mf, "memory_info", None)
if mi is None:
    print("no memory info stream")
    sys.exit()

totals = defaultdict(int)
biggest = []
for e in mi.infos:
    base, size = e.BaseAddress, e.RegionSize
    state = str(e.State)
    if base >= LOW:
        continue
    end = min(base + size, LOW)
    totals[(state, str(e.Type))] += end - base
    if "COMMIT" in state or "RESERVE" in state:
        biggest.append((end - base, base, state, str(e.Type), str(e.Protect)))

print("\n== address space below 2GB by state/type (MB) ==")
for k, v in sorted(totals.items(), key=lambda kv: -kv[1]):
    print("%-40s %8.1f" % (k, v / 2**20))
print("\n== largest used regions below 2GB ==")
for size, base, state, typ, prot in sorted(biggest, reverse=True)[:25]:
    print("%016x %8.1f MB %s %s %s" % (base, size / 2**20, state, typ, prot))
