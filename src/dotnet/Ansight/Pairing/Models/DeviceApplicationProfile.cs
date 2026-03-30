namespace Ansight.Pairing.Models;

/// <summary>
/// Describes the connected application within a <see cref="DeviceAppProfile"/>.
/// </summary>
public sealed class DeviceApplicationProfile
{
    /// <summary>
    /// Platform app identifier, such as a package id or bundle id.
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Human-readable application name.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// Operating-system process identifier for the running app, when available.
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// Human-readable application version string.
    /// </summary>
    public string? VersionName { get; set; }

    /// <summary>
    /// Machine-readable application version code.
    /// </summary>
    public string? VersionCode { get; set; }

    /// <summary>
    /// Build number associated with the installed app.
    /// </summary>
    public string? BuildNumber { get; set; }

    /// <summary>
    /// Protocol-defined environment code for the build or install variant.
    /// </summary>
    public int? EnvironmentCode { get; set; }

    /// <summary>
    /// Install source identifier, when the platform exposes one.
    /// </summary>
    public string? InstallSource { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds for the first install time, when available.
    /// </summary>
    public long? FirstInstallTimeMs { get; set; }

    /// <summary>
    /// Unix timestamp in milliseconds for the last update time, when available.
    /// </summary>
    public long? LastUpdateTimeMs { get; set; }

    /// <summary>
    /// Indicates whether the installed app is debuggable.
    /// </summary>
    public bool? Debuggable { get; set; }
}
