using System.Globalization;
using Microsoft.Win32;

namespace FpsLol.SystemIntegration;

public enum RegRoot
{
    CurrentUser,
    LocalMachine,
    Users,
}

/// <summary>Serializable registry value, used for snapshots so any value can be restored exactly.</summary>
public sealed class RegValue
{
    public RegistryValueKind Kind { get; set; }
    public long? Number { get; set; }
    public string? Text { get; set; }
    public string[]? Lines { get; set; }
    public string? Base64 { get; set; }

    public static RegValue Dword(uint value) => new() { Kind = RegistryValueKind.DWord, Number = value };
    public static RegValue Dword(int value) => Dword(unchecked((uint)value));
    public static RegValue String(string value) => new() { Kind = RegistryValueKind.String, Text = value };
    public static RegValue Binary(byte[] value) => new() { Kind = RegistryValueKind.Binary, Base64 = Convert.ToBase64String(value) };

    [System.Text.Json.Serialization.JsonIgnore]
    public byte[] Bytes => Base64 is null ? [] : Convert.FromBase64String(Base64);

    public object ToRegistryObject() => Kind switch
    {
        RegistryValueKind.DWord => unchecked((int)(uint)(Number ?? 0)),
        RegistryValueKind.QWord => Number ?? 0,
        RegistryValueKind.String or RegistryValueKind.ExpandString => Text ?? string.Empty,
        RegistryValueKind.MultiString => Lines ?? [],
        RegistryValueKind.Binary or RegistryValueKind.None or RegistryValueKind.Unknown => Bytes,
        _ => throw new NotSupportedException($"Registry kind {Kind} is not supported."),
    };

    public static RegValue FromRegistry(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => new RegValue { Kind = kind, Number = unchecked((uint)(int)value) },
        RegistryValueKind.QWord => new RegValue { Kind = kind, Number = (long)value },
        RegistryValueKind.String or RegistryValueKind.ExpandString => new RegValue { Kind = kind, Text = (string)value },
        RegistryValueKind.MultiString => new RegValue { Kind = kind, Lines = (string[])value },
        _ => new RegValue { Kind = RegistryValueKind.Binary, Base64 = Convert.ToBase64String(value as byte[] ?? []) },
    };

    public bool ValueEquals(RegValue? other)
    {
        if (other is null) return false;
        if (IsNumeric && other.IsNumeric) return Number == other.Number;
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            RegistryValueKind.String or RegistryValueKind.ExpandString => string.Equals(Text, other.Text, StringComparison.Ordinal),
            RegistryValueKind.MultiString => (Lines ?? []).SequenceEqual(other.Lines ?? []),
            _ => Bytes.AsSpan().SequenceEqual(other.Bytes),
        };
    }

    private bool IsNumeric => Kind is RegistryValueKind.DWord or RegistryValueKind.QWord;

    public override string ToString() => Kind switch
    {
        RegistryValueKind.DWord or RegistryValueKind.QWord => (Number ?? 0).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.String or RegistryValueKind.ExpandString => $"\"{Text}\"",
        RegistryValueKind.MultiString => string.Join("; ", Lines ?? []),
        _ => Convert.ToHexString(Bytes),
    };
}

public static class RegistryUtil
{
    public static RegistryKey OpenBase(RegRoot root) => RegistryKey.OpenBaseKey(root switch
    {
        RegRoot.CurrentUser => RegistryHive.CurrentUser,
        RegRoot.LocalMachine => RegistryHive.LocalMachine,
        RegRoot.Users => RegistryHive.Users,
        _ => throw new ArgumentOutOfRangeException(nameof(root)),
    }, RegistryView.Registry64);

    public static string Describe(RegRoot root, string path, string name)
    {
        var hive = root switch { RegRoot.CurrentUser => "HKCU", RegRoot.LocalMachine => "HKLM", _ => "HKU" };
        return $@"{hive}\{path}\{name}";
    }

    public static RegValue? Read(RegRoot root, string path, string name)
    {
        try
        {
            using var baseKey = OpenBase(root);
            using var key = baseKey.OpenSubKey(path, writable: false);
            if (key is null) return null;
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return value is null ? null : RegValue.FromRegistry(value, key.GetValueKind(name));
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static long? ReadNumber(RegRoot root, string path, string name) =>
        Read(root, path, name) is { Number: { } n } ? n : null;

    public static string? ReadString(RegRoot root, string path, string name) =>
        Read(root, path, name) is { Text: { } t } ? t : null;

    /// <summary>True when the key is readable (or simply absent); false when access is denied.</summary>
    public static bool CanRead(RegRoot root, string path)
    {
        try
        {
            using var baseKey = OpenBase(root);
            using var key = baseKey.OpenSubKey(path, writable: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool KeyExists(RegRoot root, string path)
    {
        try
        {
            using var baseKey = OpenBase(root);
            using var key = baseKey.OpenSubKey(path, writable: false);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void Write(RegRoot root, string path, string name, RegValue value)
    {
        using var baseKey = OpenBase(root);
        using var key = baseKey.CreateSubKey(path, writable: true)
                        ?? throw new InvalidOperationException($"Could not open {path} for writing.");
        key.SetValue(name, value.ToRegistryObject(), value.Kind);
    }

    public static void DeleteValue(RegRoot root, string path, string name)
    {
        using var baseKey = OpenBase(root);
        using var key = baseKey.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
