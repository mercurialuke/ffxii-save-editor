// Minimal process memory tool for locating FFXII settings.
//   memtool scan  <process> <hex pattern>      list addresses where the bytes occur
//   memtool read  <process> <hex addr> <len>   hex dump
//   memtool write <process> <hex addr> <hex bytes>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

static class MemTool
{
    [StructLayout(LayoutKind.Sequential)]
    struct MBI
    {
        public IntPtr BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State, Protect, Type;
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr read);
    [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, IntPtr size, out IntPtr written);
    [DllImport("kernel32.dll")] static extern IntPtr VirtualQueryEx(IntPtr h, IntPtr addr, out MBI info, IntPtr len);

    static byte[] Hex(string s)
    {
        s = s.Replace(" ", "");
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = byte.Parse(s.Substring(2 * i, 2), NumberStyles.HexNumber);
        return b;
    }

    static int Main(string[] a)
    {
        var procs = Process.GetProcessesByName(a[1]);
        if (procs.Length == 0) { Console.Error.WriteLine("process not found"); return 1; }
        var pid = procs[0].Id;
        bool write = a[0] == "write";
        // PROCESS_VM_READ | PROCESS_QUERY_INFORMATION (| PROCESS_VM_WRITE | PROCESS_VM_OPERATION)
        var h = OpenProcess(write ? 0x0438u : 0x0410u, false, pid);
        if (h == IntPtr.Zero) { Console.Error.WriteLine("OpenProcess failed"); return 1; }

        if (a[0] == "scan")
        {
            var pat = Hex(a[2]);
            long addr = 0;
            MBI m;
            var hits = new List<long>();
            while (VirtualQueryEx(h, new IntPtr(addr), out m, new IntPtr(Marshal.SizeOf(typeof(MBI)))) != IntPtr.Zero)
            {
                long size = m.RegionSize.ToInt64();
                bool readable = m.State == 0x1000 && (m.Protect & 0x100) == 0 && (m.Protect & 0x01) == 0;
                if (readable && size < (1L << 31))
                {
                    var buf = new byte[size];
                    IntPtr got;
                    if (ReadProcessMemory(h, m.BaseAddress, buf, new IntPtr(size), out got))
                    {
                        int n = (int)got.ToInt64();
                        for (int i = 0; i + pat.Length <= n; i++)
                        {
                            int j = 0;
                            while (j < pat.Length && buf[i + j] == pat[j]) j++;
                            if (j == pat.Length) hits.Add(m.BaseAddress.ToInt64() + i);
                        }
                    }
                }
                addr = m.BaseAddress.ToInt64() + size;
                if (addr <= 0) break;
            }
            foreach (var x in hits) Console.WriteLine("{0:x}", x);
            Console.WriteLine("{0} hit(s)", hits.Count);
        }
        else if (a[0] == "read")
        {
            long at = long.Parse(a[2], NumberStyles.HexNumber);
            int len = int.Parse(a[3]);
            var buf = new byte[len];
            IntPtr got;
            ReadProcessMemory(h, new IntPtr(at), buf, new IntPtr(len), out got);
            for (int i = 0; i < len; i += 16)
                Console.WriteLine("{0:x}: {1}", at + i, BitConverter.ToString(buf, i, Math.Min(16, len - i)).Replace("-", " "));
        }
        else if (write)
        {
            long at = long.Parse(a[2], NumberStyles.HexNumber);
            var data = Hex(a[3]);
            IntPtr wrote;
            bool ok = WriteProcessMemory(h, new IntPtr(at), data, new IntPtr(data.Length), out wrote);
            Console.WriteLine(ok ? "wrote " + wrote + " bytes" : "write failed");
        }
        return 0;
    }
}
