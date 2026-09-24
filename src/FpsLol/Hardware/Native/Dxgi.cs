using System.Runtime.InteropServices;

namespace FpsLol.Hardware.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;

    public string Key => $"{HighPart:X8}_{LowPart:X8}";
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DXGI_ADAPTER_DESC1
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Description;
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public nuint DedicatedVideoMemory;
    public nuint DedicatedSystemMemory;
    public nuint SharedSystemMemory;
    public LUID AdapterLuid;
    public uint Flags;
}

[ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIAdapter1
{
    [PreserveSig] int SetPrivateData(ref Guid name, uint size, IntPtr data);
    [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
    [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, IntPtr data);
    [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
    [PreserveSig] int EnumOutputs(uint output, out IntPtr ppOutput);
    [PreserveSig] int GetDesc(IntPtr desc);
    [PreserveSig] int CheckInterfaceSupport(ref Guid interfaceName, out long umdVersion);
    [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
}

[ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDXGIFactory1
{
    [PreserveSig] int SetPrivateData(ref Guid name, uint size, IntPtr data);
    [PreserveSig] int SetPrivateDataInterface(ref Guid name, IntPtr unknown);
    [PreserveSig] int GetPrivateData(ref Guid name, ref uint size, IntPtr data);
    [PreserveSig] int GetParent(ref Guid riid, out IntPtr parent);
    [PreserveSig] int EnumAdapters(uint adapter, out IntPtr ppAdapter);
    [PreserveSig] int MakeWindowAssociation(IntPtr hwnd, uint flags);
    [PreserveSig] int GetWindowAssociation(out IntPtr hwnd);
    [PreserveSig] int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
    [PreserveSig] int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
    [PreserveSig] int EnumAdapters1(uint adapter, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1? ppAdapter);
    [PreserveSig] [return: MarshalAs(UnmanagedType.Bool)] bool IsCurrent();
}

internal sealed record DxgiAdapter(string Name, uint VendorId, ulong DedicatedVideoMemory, string LuidKey);

internal static class Dxgi
{
    private const uint SoftwareAdapterFlag = 2;

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1? factory);

    public static IReadOnlyList<DxgiAdapter> EnumerateAdapters()
    {
        var list = new List<DxgiAdapter>();
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var factory) != 0 || factory is null) return list;
        try
        {
            for (uint i = 0; ; i++)
            {
                if (factory.EnumAdapters1(i, out var adapter) != 0 || adapter is null) break;
                try
                {
                    if (adapter.GetDesc1(out var desc) != 0) continue;
                    if ((desc.Flags & SoftwareAdapterFlag) != 0 || desc.VendorId == 0x1414) continue; // Microsoft Basic Render Driver
                    if (list.Any(a => a.LuidKey == desc.AdapterLuid.Key)) continue;
                    list.Add(new DxgiAdapter(desc.Description.Trim(), desc.VendorId, desc.DedicatedVideoMemory, desc.AdapterLuid.Key));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
        return list;
    }
}
