namespace Ansight.Pairing.Models;

public sealed class DeviceApplicationProfile
{
    public string? AppId { get; set; }
    public string? AppName { get; set; }
    public string? VersionName { get; set; }
    public string? VersionCode { get; set; }
    public string? BuildNumber { get; set; }
    public int? EnvironmentCode { get; set; }
    public string? InstallSource { get; set; }
    public long? FirstInstallTimeMs { get; set; }
    public long? LastUpdateTimeMs { get; set; }
    public bool? Debuggable { get; set; }
}
