import ntpath
import struct
import sys

from minidump.minidumpfile import MinidumpFile

mf = MinidumpFile.parse(sys.argv[1])
r = mf.get_reader()
mods = sorted(mf.modules.modules, key=lambda m: m.baseaddress) if mf.modules else []


def mod_of(addr):
    for m in mods:
        if m.baseaddress <= addr < m.baseaddress + m.size:
            return "%s+0x%x" % (ntpath.basename(m.name), addr - m.baseaddress)
    return None


if mf.exception:
    for e in mf.exception.exception_records:
        rec = e.ExceptionRecord
        code = getattr(rec.ExceptionCode, "value", rec.ExceptionCode)
        print("thread", hex(e.ThreadId), "exception code", hex(code) if isinstance(code, int) else code,
              "at", hex(rec.ExceptionAddress), mod_of(rec.ExceptionAddress))
        print("params", [hex(p) for p in rec.ExceptionInformation[:rec.NumberParameters]])
        th = [t for t in mf.threads.threads if t.ThreadId == e.ThreadId][0]
        stack = th.Stack
        data = r.read(stack.StartOfMemoryRange, min(stack.DataSize, 0x10000))
        seen = []
        for i in range(0, len(data) - 8, 8):
            v = struct.unpack_from("<Q", data, i)[0]
            m = mod_of(v)
            if m and (not seen or seen[-1] != m):
                seen.append(m)
        print("stack (return addresses found on the faulting thread's stack, innermost first):")
        for s in seen[:60]:
            print("   ", s)
else:
    print("no exception stream")

keys = ("d3d", "dxgi", "nv", "amd", "atiu", "overlay", "hook", "rtss", "discord", "reshade", "steam", "sharpdx", "dinput", "xinput", "sound", "audio", "mmdev")
print("modules of interest:", [ntpath.basename(m.name) for m in mods if any(k in m.name.lower() for k in keys)])
