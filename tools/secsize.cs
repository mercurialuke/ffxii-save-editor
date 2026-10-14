// Prints the size of named shared memory sections (opens read-only, maps, queries).
using System;
using System.Runtime.InteropServices;

static class SecSize
{
    [StructLayout(LayoutKind.Sequential)]
    struct MBI
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr OpenFileMappingW(uint access, bool inherit, string name);
    [DllImport("kernel32.dll")] static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint hi, uint lo, UIntPtr n);
    [DllImport("kernel32.dll")] static extern bool UnmapViewOfFile(IntPtr p);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualQuery(IntPtr addr, out MBI info, IntPtr len);

    static void Main(string[] names)
    {
        foreach (var n in names)
        {
            var h = OpenFileMappingW(0x0004 /* FILE_MAP_READ */, false, n);
            if (h == IntPtr.Zero) { Console.WriteLine("{0,-70} not found (err {1})", n, Marshal.GetLastWin32Error()); continue; }
            var p = MapViewOfFile(h, 0x0004, 0, 0, UIntPtr.Zero);
            if (p == IntPtr.Zero) { Console.WriteLine("{0,-70} map failed", n); CloseHandle(h); continue; }
            long total = 0;
            var a = p;
            MBI m;
            while (VirtualQuery(a, out m, new IntPtr(Marshal.SizeOf(typeof(MBI)))) != IntPtr.Zero && m.AllocationBase == p)
            {
                total += m.RegionSize.ToInt64();
                a = new IntPtr(m.BaseAddress.ToInt64() + m.RegionSize.ToInt64());
            }
            Console.WriteLine("{0,-70} {1,10:N0} KB", n, total / 1024);
            UnmapViewOfFile(p);
            CloseHandle(h);
        }
    }
}
