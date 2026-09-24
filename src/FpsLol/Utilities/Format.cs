using System.Globalization;

namespace FpsLol.Utilities;

public static class Format
{
    public const string NotAvailable = "N/A";

    public static string Bytes(long bytes, int decimals = 1)
    {
        if (bytes < 0) return NotAvailable;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes} B"
            : value.ToString("F" + decimals, CultureInfo.InvariantCulture) + " " + units[unit];
    }

    public static string Bytes(ulong bytes, int decimals = 1) => Bytes((long)Math.Min(bytes, long.MaxValue), decimals);

    public static string Percent(double? value) =>
        value is { } v && !double.IsNaN(v) ? Math.Round(v).ToString(CultureInfo.InvariantCulture) + "%" : NotAvailable;

    public static string Temperature(double? celsius) =>
        celsius is { } c && c > 0 && c < 150 ? Math.Round(c).ToString(CultureInfo.InvariantCulture) + " °C" : NotAvailable;

    public static string Mhz(double? mhz)
    {
        if (mhz is not { } m || m <= 0) return NotAvailable;
        return m >= 1000
            ? (m / 1000).ToString("F2", CultureInfo.InvariantCulture) + " GHz"
            : Math.Round(m).ToString(CultureInfo.InvariantCulture) + " MHz";
    }

    public static string Duration(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
        return $"{t.Minutes}m {t.Seconds}s";
    }

    public static string LinkSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return NotAvailable;
        if (bitsPerSecond >= 1_000_000_000) return (bitsPerSecond / 1_000_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + " Gbps";
        return (bitsPerSecond / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + " Mbps";
    }

    public static string OrNa(string? value) => string.IsNullOrWhiteSpace(value) ? NotAvailable : value.Trim();
}
