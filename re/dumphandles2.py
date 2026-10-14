import sys
from minidump.minidumpfile import MinidumpFile
mf = MinidumpFile.parse(sys.argv[1])
for h in mf.handles.handles:
    t = getattr(h, "TypeName", None) or ""
    n = getattr(h, "ObjectName", None) or ""
    if t in ("File", "Section") and n:
        print(t, n)
