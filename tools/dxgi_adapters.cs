using System;
using System.Runtime.InteropServices;

// Lists DXGI adapters with their LUIDs and outputs (read-only).
class P
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow; public int LuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DXGI_OUTPUT_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public int Left, Top, Right, Bottom;
        public int AttachedToDesktop; public int Rotation; public IntPtr Monitor;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIFactory1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        [PreserveSig] int EnumAdapters(uint i, out IntPtr a);
        void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();
        [PreserveSig] int EnumAdapters1(uint i, out IDXGIAdapter1 a);
        bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIAdapter1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        [PreserveSig] int EnumOutputs(uint i, out IDXGIOutput o);
        void GetDesc(); void CheckInterfaceSupport();
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 d);
    }

    [ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXGIOutput
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
        [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC d);
    }

    [DllImport("dxgi.dll")] static extern int CreateDXGIFactory1(ref Guid riid, out IDXGIFactory1 f);

    static void Main()
    {
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        IDXGIFactory1 f;
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out f));
        for (uint i = 0; ; i++)
        {
            IDXGIAdapter1 a;
            if (f.EnumAdapters1(i, out a) != 0) break;
            DXGI_ADAPTER_DESC1 d;
            a.GetDesc1(out d);
            Console.WriteLine("adapter {0}: {1} DeviceID={2} LUIDLow={3} LUIDHigh={4}", i, d.Description, d.DeviceId, d.LuidLow, d.LuidHigh);
            for (uint j = 0; ; j++)
            {
                IDXGIOutput o;
                if (a.EnumOutputs(j, out o) != 0) break;
                DXGI_OUTPUT_DESC od;
                o.GetDesc(out od);
                Console.WriteLine("   output {0}: {1} {2}x{3} at {4},{5}", j, od.DeviceName, od.Right - od.Left, od.Bottom - od.Top, od.Left, od.Top);
            }
        }
    }
}
