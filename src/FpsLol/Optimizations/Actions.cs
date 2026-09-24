using System.ServiceProcess;
using FpsLol.SystemIntegration;

namespace FpsLol.Optimizations;

// ============================================================================ Registry value

public sealed class RegistryValueAction : SystemAction
{
    public RegistryValueAction() { }

    public RegistryValueAction(RegRoot root, string path, string name, RegValue? value)
    {
        Root = root;
        Path = path;
        Name = name;
        Value = value;
    }

    public RegRoot Root { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Target value; <c>null</c> removes the value (restores the Windows default behaviour).</summary>
    public RegValue? Value { get; set; }

    public override bool RequiresAdmin => Root != RegRoot.CurrentUser;

    public override string Describe() => Value is null
        ? $"Remove {RegistryUtil.Describe(Root, Path, Name)}"
        : $"Set {RegistryUtil.Describe(Root, Path, Name)} = {Value}";

    public override string? Validate() => SafetyPolicy.CheckRegistry(Root, Path, Name);

    public override ActionSnapshot Capture() => RegistryValueSnapshot.Take(Root, Path, Name);

    public override void Execute()
    {
        if (Value is null) RegistryUtil.DeleteValue(Root, Path, Name);
        else RegistryUtil.Write(Root, Path, Name, Value);
    }

    public override bool Verify()
    {
        var now = RegistryUtil.Read(Root, Path, Name);
        return Value is null ? now is null : Value.ValueEquals(now);
    }
}

public sealed class RegistryValueSnapshot : ActionSnapshot
{
    public RegRoot Root { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary><c>null</c> means the value did not exist before the change.</summary>
    public RegValue? Previous { get; set; }

    public static RegistryValueSnapshot Take(RegRoot root, string path, string name) => new()
    {
        Root = root,
        Path = path,
        Name = name,
        Previous = RegistryUtil.Read(root, path, name),
    };

    public override bool RequiresAdmin => Root != RegRoot.CurrentUser;

    public override string Describe() => Previous is null
        ? $"Remove {RegistryUtil.Describe(Root, Path, Name)} (did not exist before)"
        : $"Restore {RegistryUtil.Describe(Root, Path, Name)} = {Previous}";

    public override string? Validate() => SafetyPolicy.CheckRegistry(Root, Path, Name);

    public override void Restore()
    {
        if (Previous is null) RegistryUtil.DeleteValue(Root, Path, Name);
        else RegistryUtil.Write(Root, Path, Name, Previous);
    }

    public override bool VerifyRestored()
    {
        var now = RegistryUtil.Read(Root, Path, Name);
        return Previous is null ? now is null : Previous.ValueEquals(now);
    }
}

// ============================================================================ "key=value;" string maps

/// <summary>
/// Edits one entry inside a "Key=Value;Key2=Value2;" string value (DirectX user GPU preferences),
/// preserving every other entry.
/// </summary>
public sealed class RegistryStringMapAction : SystemAction
{
    public RegistryStringMapAction() { }

    public RegistryStringMapAction(RegRoot root, string path, string name, string key, string? value)
    {
        Root = root;
        Path = path;
        Name = name;
        Key = key;
        Value = value;
    }

    public RegRoot Root { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }

    public override bool RequiresAdmin => Root != RegRoot.CurrentUser;

    public override string Describe() => $"Set {Key}={Value ?? "(removed)"} in {RegistryUtil.Describe(Root, Path, Name)}";

    public override string? Validate() => SafetyPolicy.CheckRegistry(Root, Path, Name);

    public override ActionSnapshot Capture() => RegistryValueSnapshot.Take(Root, Path, Name);

    public override void Execute()
    {
        var map = Parse(RegistryUtil.ReadString(Root, Path, Name));
        var index = map.FindIndex(kv => string.Equals(kv.Key, Key, StringComparison.OrdinalIgnoreCase));
        if (Value is null)
        {
            if (index >= 0) map.RemoveAt(index);
        }
        else if (index >= 0)
        {
            map[index] = new(Key, Value);
        }
        else
        {
            map.Add(new(Key, Value));
        }

        if (map.Count == 0) RegistryUtil.DeleteValue(Root, Path, Name);
        else RegistryUtil.Write(Root, Path, Name, RegValue.String(Serialize(map)));
    }

    public override bool Verify() => string.Equals(Get(RegistryUtil.ReadString(Root, Path, Name), Key), Value, StringComparison.OrdinalIgnoreCase);

    public static string? Get(string? raw, string key) =>
        Parse(raw).FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    public static List<KeyValuePair<string, string>> Parse(string? raw)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            result.Add(new(part[..eq].Trim(), part[(eq + 1)..].Trim()));
        }
        return result;
    }

    private static string Serialize(IEnumerable<KeyValuePair<string, string>> map) =>
        string.Concat(map.Select(kv => $"{kv.Key}={kv.Value};"));
}

// ============================================================================ Per-executable compatibility layers

/// <summary>Adds/removes one compatibility flag (e.g. DISABLEDXMAXIMIZEDWINDOWEDMODE) for a single executable.</summary>
public sealed class AppCompatLayerAction : SystemAction
{
    public const string LayersPath = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    public AppCompatLayerAction() { }

    public AppCompatLayerAction(string exePath, string flag, bool enabled)
    {
        ExePath = exePath;
        Flag = flag;
        Enabled = enabled;
    }

    public string ExePath { get; set; } = string.Empty;
    public string Flag { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    public override bool RequiresAdmin => false;

    public override string Describe() => $"{(Enabled ? "Add" : "Remove")} compatibility flag {Flag} for {System.IO.Path.GetFileName(ExePath)}";

    public override string? Validate()
    {
        if (!ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Compatibility flags can only target .exe files.";
        return SafetyPolicy.CheckRegistry(RegRoot.CurrentUser, LayersPath, ExePath);
    }

    public override ActionSnapshot Capture() => RegistryValueSnapshot.Take(RegRoot.CurrentUser, LayersPath, ExePath);

    public static bool HasFlag(string exePath, string flag) =>
        Tokens(RegistryUtil.ReadString(RegRoot.CurrentUser, LayersPath, exePath)).Contains(flag, StringComparer.OrdinalIgnoreCase);

    public override void Execute()
    {
        var tokens = Tokens(RegistryUtil.ReadString(RegRoot.CurrentUser, LayersPath, ExePath));
        tokens.RemoveAll(t => string.Equals(t, Flag, StringComparison.OrdinalIgnoreCase));
        if (Enabled) tokens.Add(Flag);

        if (tokens.Count == 0) RegistryUtil.DeleteValue(RegRoot.CurrentUser, LayersPath, ExePath);
        else RegistryUtil.Write(RegRoot.CurrentUser, LayersPath, ExePath, RegValue.String("~ " + string.Join(' ', tokens)));
    }

    public override bool Verify() => HasFlag(ExePath, Flag) == Enabled;

    private static List<string> Tokens(string? raw) =>
        (raw ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t != "~").ToList();
}

// ============================================================================ Services

public sealed class ServiceStartAction : SystemAction
{
    public ServiceStartAction() { }

    public ServiceStartAction(string serviceName, ServiceStartMode mode, bool stopNow)
    {
        ServiceName = serviceName;
        Mode = mode;
        StopNow = stopNow;
    }

    public string ServiceName { get; set; } = string.Empty;
    public ServiceStartMode Mode { get; set; }
    public bool StopNow { get; set; }

    public override bool RequiresAdmin => true;

    public override string Describe() => $"Set service {ServiceName} startup to {Mode}";

    public override string? Validate() => SafetyPolicy.CheckService(ServiceName);

    public override ActionSnapshot Capture() => new ServiceSnapshot
    {
        ServiceName = ServiceName,
        Mode = ServiceConfig.GetStartMode(ServiceName),
        WasRunning = ServiceConfig.IsRunning(ServiceName),
    };

    public override void Execute()
    {
        ServiceConfig.SetStartMode(ServiceName, Mode);
        if (StopNow) ServiceConfig.Stop(ServiceName, TimeSpan.FromSeconds(20));
    }

    public override bool Verify() => ServiceConfig.GetStartMode(ServiceName) == Mode;
}

public sealed class ServiceSnapshot : ActionSnapshot
{
    public string ServiceName { get; set; } = string.Empty;
    public ServiceStartMode Mode { get; set; }
    public bool WasRunning { get; set; }

    public override bool RequiresAdmin => true;

    public override string Describe() => $"Restore service {ServiceName} startup to {Mode}";

    public override string? Validate() => SafetyPolicy.CheckService(ServiceName);

    public override void Restore()
    {
        ServiceConfig.SetStartMode(ServiceName, Mode);
        if (WasRunning && Mode != ServiceStartMode.Disabled)
        {
            try { ServiceConfig.Start(ServiceName, TimeSpan.FromSeconds(20)); }
            catch { /* Trigger-start services may refuse a manual start; the start type is what matters. */ }
        }
    }

    public override bool VerifyRestored() => ServiceConfig.GetStartMode(ServiceName) == Mode;
}

// ============================================================================ Power scheme

public sealed class PowerSchemeAction : SystemAction
{
    private Guid _resolved;

    public PowerSchemeAction() { }

    public PowerSchemeAction(Guid target, Guid? createFromTemplateIfMissing = null)
    {
        Target = target;
        Template = createFromTemplateIfMissing;
    }

    public Guid Target { get; set; }

    /// <summary>If the target scheme does not exist, a copy of this built-in template is created and activated.</summary>
    public Guid? Template { get; set; }

    public Guid? CreatedScheme { get; set; }

    public override bool RequiresAdmin => false;

    public override string Describe() => $"Activate power plan {Target}";

    public override ActionSnapshot Capture() => new PowerSchemeSnapshot { Previous = PowerPlans.GetActive() };

    public override void Execute()
    {
        _resolved = Target;
        if (!PowerPlans.Exists(Target))
        {
            if (Template is not { } template)
                throw new InvalidOperationException("The selected power plan no longer exists.");
            _resolved = PowerPlans.Duplicate(template);
            CreatedScheme = _resolved;
        }
        PowerPlans.SetActive(_resolved);
    }

    public override bool Verify() => PowerPlans.GetActive() == _resolved;
}

public sealed class PowerSchemeSnapshot : ActionSnapshot
{
    public Guid Previous { get; set; }

    public override bool RequiresAdmin => false;

    public override string Describe() => $"Re-activate power plan {Previous}";

    public override void Restore()
    {
        if (!PowerPlans.Exists(Previous))
            throw new InvalidOperationException("The previous power plan no longer exists on this system.");
        PowerPlans.SetActive(Previous);
    }

    public override bool VerifyRestored() => PowerPlans.GetActive() == Previous;
}

// ============================================================================ SystemParametersInfo

public sealed class SpiAction : SystemAction
{
    public SpiAction() { }

    public SpiAction(SpiSetting setting, int[] values)
    {
        Setting = setting;
        Values = values;
    }

    public SpiSetting Setting { get; set; }
    public int[] Values { get; set; } = [];

    public override bool RequiresAdmin => false;

    public override string Describe() => $"Set {Setting} = [{string.Join(", ", Values)}]";

    public override ActionSnapshot Capture() => new SpiSnapshot { Setting = Setting, Values = SystemParametersUtil.Get(Setting) };

    public override void Execute() => SystemParametersUtil.Set(Setting, Values);

    public override bool Verify() => SystemParametersUtil.Matches(Setting, Values, SystemParametersUtil.Get(Setting));
}

public sealed class SpiSnapshot : ActionSnapshot
{
    public SpiSetting Setting { get; set; }
    public int[] Values { get; set; } = [];

    public override bool RequiresAdmin => false;

    public override string Describe() => $"Restore {Setting} = [{string.Join(", ", Values)}]";

    public override void Restore() => SystemParametersUtil.Set(Setting, Values);

    public override bool VerifyRestored() => SystemParametersUtil.Matches(Setting, Values, SystemParametersUtil.Get(Setting));
}
