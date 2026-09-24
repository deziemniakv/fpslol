using System.Runtime.InteropServices;

namespace FpsLol.Hardware.Native;

/// <summary>
/// Kernel-mode thunk queries (the same vendor-neutral source Task Manager uses) for
/// GPU temperature and hardware-accelerated GPU scheduling capabilities. No drivers are installed.
/// </summary>
internal static class D3dkmt
{
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;
    private const int KMTQAITYPE_WDDM_2_7_CAPS = 70;

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO
    {
        public uint hAdapter;
        public LUID AdapterLuid;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2
    {
        public uint NumAdapters;
        public IntPtr pAdapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature; // deci-Celsius
        public byte PowerStateOverride;
    }

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 args);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO args);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER args);

    public sealed record AdapterReading(string LuidKey, double? TemperatureC, int? FanRpm, bool? HwSchSupported, bool? HwSchEnabled);

    public static IReadOnlyList<AdapterReading> Query(bool includeScheduling)
    {
        var results = new List<AdapterReading>();
        var enumArgs = new D3DKMT_ENUMADAPTERS2();
        try
        {
            if (D3DKMTEnumAdapters2(ref enumArgs) != 0 || enumArgs.NumAdapters == 0) return results;
        }
        catch (EntryPointNotFoundException)
        {
            return results;
        }

        int size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
        var buffer = Marshal.AllocHGlobal(size * (int)enumArgs.NumAdapters);
        try
        {
            enumArgs.pAdapters = buffer;
            if (D3DKMTEnumAdapters2(ref enumArgs) != 0) return results;

            for (int i = 0; i < enumArgs.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(buffer + i * size);
                try
                {
                    double? temp = null;
                    int? fan = null;
                    if (TryQuery(info.hAdapter, KMTQAITYPE_ADAPTERPERFDATA, out D3DKMT_ADAPTER_PERFDATA perf))
                    {
                        if (perf.Temperature is > 0 and < 1500) temp = perf.Temperature / 10.0;
                        if (perf.FanRPM is > 0 and < 20000) fan = (int)perf.FanRPM;
                    }

                    bool? schSupported = null, schEnabled = null;
                    if (includeScheduling && TryQuery(info.hAdapter, KMTQAITYPE_WDDM_2_7_CAPS, out uint caps))
                    {
                        schSupported = (caps & 0x1) != 0;
                        schEnabled = (caps & 0x2) != 0;
                    }

                    results.Add(new AdapterReading(info.AdapterLuid.Key, temp, fan, schSupported, schEnabled));
                }
                finally
                {
                    var close = new D3DKMT_CLOSEADAPTER { hAdapter = info.hAdapter };
                    D3DKMTCloseAdapter(ref close);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return results;
    }

    private static bool TryQuery<T>(uint adapter, int type, out T value) where T : struct
    {
        value = default;
        int size = Marshal.SizeOf<T>();
        var data = Marshal.AllocHGlobal(size);
        try
        {
            // Zero-initialise the input (PhysicalAdapterIndex = 0 for perf data).
            for (int i = 0; i < size; i++) Marshal.WriteByte(data, i, 0);
            var args = new D3DKMT_QUERYADAPTERINFO
            {
                hAdapter = adapter,
                Type = type,
                pPrivateDriverData = data,
                PrivateDriverDataSize = (uint)size,
            };
            if (D3DKMTQueryAdapterInfo(ref args) != 0) return false;
            value = Marshal.PtrToStructure<T>(data);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }
}
