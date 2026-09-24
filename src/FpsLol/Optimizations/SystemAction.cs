using System.Text.Json.Serialization;
using FpsLol.Networking;

namespace FpsLol.Optimizations;

/// <summary>
/// A single, reversible system change. Every action can capture the current state (snapshot),
/// execute, and verify that the change really took effect.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(RegistryValueAction), "registry")]
[JsonDerivedType(typeof(RegistryStringMapAction), "registry-map")]
[JsonDerivedType(typeof(AppCompatLayerAction), "appcompat-layer")]
[JsonDerivedType(typeof(ServiceStartAction), "service")]
[JsonDerivedType(typeof(PowerSchemeAction), "power")]
[JsonDerivedType(typeof(SpiAction), "spi")]
[JsonDerivedType(typeof(DnsAction), "dns")]
[JsonDerivedType(typeof(TcpAutoTuningAction), "tcp-autotuning")]
[JsonDerivedType(typeof(AdapterPowerAction), "adapter-power")]
public abstract class SystemAction
{
    [JsonIgnore]
    public abstract bool RequiresAdmin { get; }

    public abstract string Describe();

    /// <summary>Safety policy check. Returns an error message when the action is not allowed.</summary>
    public virtual string? Validate() => null;

    public abstract ActionSnapshot Capture();

    public abstract void Execute();

    public abstract bool Verify();
}

/// <summary>The state of a setting before FPS.LOL changed it. Persisted in the Restore Center history.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(RegistryValueSnapshot), "registry")]
[JsonDerivedType(typeof(ServiceSnapshot), "service")]
[JsonDerivedType(typeof(PowerSchemeSnapshot), "power")]
[JsonDerivedType(typeof(SpiSnapshot), "spi")]
[JsonDerivedType(typeof(DnsSnapshot), "dns")]
[JsonDerivedType(typeof(TcpAutoTuningSnapshot), "tcp-autotuning")]
[JsonDerivedType(typeof(AdapterPowerSnapshot), "adapter-power")]
public abstract class ActionSnapshot
{
    [JsonIgnore]
    public abstract bool RequiresAdmin { get; }

    public abstract string Describe();

    public virtual string? Validate() => null;

    public abstract void Restore();

    public abstract bool VerifyRestored();
}
