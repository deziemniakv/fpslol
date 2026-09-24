using System.Runtime.InteropServices;

namespace FpsLol.Hardware.Native;

/// <summary>
/// Minimal wrapper over the Performance Data Helper API. Uses English counter paths so it works
/// on every Windows display language, and supports wildcard instances (e.g. per-GPU-engine counters).
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Explicit)]
    private struct PDH_FMT_COUNTERVALUE
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double doubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;

    public PdhQuery()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) _query = IntPtr.Zero;
    }

    public bool IsValid => _query != IntPtr.Zero;

    /// <summary>Returns a counter handle, or <see cref="IntPtr.Zero"/> if the counter does not exist on this system.</summary>
    public IntPtr Add(string englishPath)
    {
        if (!IsValid) return IntPtr.Zero;
        return PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero;
    }

    public bool Collect() => IsValid && PdhCollectQueryData(_query) == 0;

    public double? Get(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value) != 0) return null;
        return value.CStatus is 0 or 1 ? value.doubleValue : null;
    }

    public List<(string Instance, double Value)> GetArray(IntPtr counter)
    {
        var list = new List<(string, double)>();
        if (counter == IntPtr.Zero) return list;

        uint size = 0;
        var status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return list;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out var count, buffer) != 0) return list;
            int itemSize = IntPtr.Size + Marshal.SizeOf<PDH_FMT_COUNTERVALUE>();
            for (int i = 0; i < count; i++)
            {
                var item = buffer + i * itemSize;
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? string.Empty;
                var value = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE>(item + IntPtr.Size);
                if (value.CStatus is 0 or 1) list.Add((name, value.doubleValue));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return list;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}
