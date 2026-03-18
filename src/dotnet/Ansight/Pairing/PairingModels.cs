using System.Net;

namespace Ansight.Pairing;

public sealed class PairingChallenge
{
    public required string Alg { get; set; }
    public required string ChallengePubKey { get; set; }
    public required bool RequireProofOnFirstPair { get; set; }
}

public sealed class PairingConfig
{
    public required string Schema { get; set; }
    public required string ConfigId { get; set; }
    public required string AppId { get; set; }
    public required string AppName { get; set; }
    public required DateTimeOffset IssuedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string OneTimeToken { get; set; }
    public required PairingHost Host { get; set; }
    public required PairingChallenge Challenge { get; set; }
    public required PairingTrust Trust { get; set; }
    public required string Signature { get; set; }
}

public sealed class PairingHost
{
    public string? HostId { get; set; }
    public string? HostName { get; set; }
    public int DiscoveryPort { get; set; } = PairingProtocolDefaults.DiscoveryPort;
    public required string HostPubKey { get; set; }
    public required string HostPubKeyFingerprint { get; set; }
}

public sealed class PairingTrust
{
    public required string Mode { get; set; }
    public required bool RequireTokenOnFirstPair { get; set; }
    public required bool AllowLanDiscovery { get; set; }
}

public sealed class PairingDiscoveryHint
{
    public const string SchemaName = "ansight.discovery-hint.v1";

    public required string Schema { get; set; } = SchemaName;
    public string? Source { get; set; }
    public string? HostAddress { get; set; }
    public string? HostName { get; set; }
    public string? WifiName { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}

public sealed class PairingConnectionHint
{
    public const string SchemaName = "ansight.pairing-connection-hint.v1";

    public required string Schema { get; set; } = SchemaName;
    public string? Source { get; set; }
    public required string ConfigId { get; set; }
    public required DateTimeOffset IssuedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string OneTimeToken { get; set; }
    public required PairingChallenge Challenge { get; set; }
}

public sealed class PairingQrConnectionPayload
{
    public const string SchemaName = "ansight.qr-pairing-connection.v1";

    public required string Schema { get; set; } = SchemaName;
    public required PairingConnectionHint Connection { get; set; }
    public PairingDiscoveryHint? Discovery { get; set; }
}

public sealed class PairingBootstrapDocument
{
    public const string SchemaName = "ansight.pairing-bootstrap.v1";

    public required string Schema { get; set; } = SchemaName;
    public required PairingConfig PairingConfig { get; set; }
    public PairingDiscoveryHint? Discovery { get; set; }
    public PairingConnectionHint? ConnectionHint { get; set; }
}

public enum PairingDiscoveryMode
{
    ConfiguredHint = 0,
    BasicManual = 1
}

public sealed class PairingConnectionOptions
{
    public PairingDiscoveryMode DiscoveryMode { get; set; } = PairingDiscoveryMode.ConfiguredHint;
    public string? ManualHostAddress { get; set; }
    public DeviceAppProfile? DeviceAppProfile { get; set; }
}

public sealed class DeviceAppProfile
{
    public string Type { get; set; } = "DeviceAppProfile";
    public string Schema { get; set; } = "ansight.device-app-profile.v1";
    public long SentAt { get; set; }
    public int ReasonCode { get; set; } = 1;
    public int ProfileSeq { get; set; } = 1;
    public DeviceProfile? Device { get; set; }
    public DeviceApplicationProfile? App { get; set; }
    public DeviceRuntimeProfile? Runtime { get; set; }
    public DeviceGraphicsProfile? Graphics { get; set; }
    public Dictionary<string, string>? Permissions { get; set; }
    public List<string>? Tags { get; set; }
}

public sealed class DeviceProfile
{
    public string? Manufacturer { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Product { get; set; }
    public int? DeviceClassCode { get; set; }
    public bool? IsEmulator { get; set; }
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
    public string? OsBuild { get; set; }
    public int? ApiLevel { get; set; }
    public string? CpuArch { get; set; }
    public int? CpuCoreCount { get; set; }
    public List<string>? AbiList { get; set; }
    public string? ChipModel { get; set; }
    public long? MemoryTotalMb { get; set; }
    public long? MemoryFreeMb { get; set; }
    public long? StorageTotalMb { get; set; }
    public long? StorageFreeMb { get; set; }
    public DeviceBatteryProfile? Battery { get; set; }
    public DeviceDisplayProfile? Display { get; set; }
    public DeviceGpuProfile? Gpu { get; set; }
    public DeviceNetworkProfile? Network { get; set; }
    public DeviceThermalProfile? Thermal { get; set; }
}

public sealed class DeviceBatteryProfile
{
    public int? LevelPct { get; set; }
    public int? StateCode { get; set; }
    public int? HealthCode { get; set; }
    public double? TemperatureC { get; set; }
}

public sealed class DeviceDisplayProfile
{
    public int? WidthPx { get; set; }
    public int? HeightPx { get; set; }
    public int? DensityDpi { get; set; }
    public double? RefreshRateHz { get; set; }
    public bool? HdrSupported { get; set; }
}

public sealed class DeviceGpuProfile
{
    public string? Vendor { get; set; }
    public string? Model { get; set; }
    public string? Renderer { get; set; }
    public int? ApiCode { get; set; }
    public string? DriverVersion { get; set; }
    public long? VramMb { get; set; }
    public string? FeatureLevel { get; set; }
}

public sealed class DeviceNetworkProfile
{
    public int? TransportCode { get; set; }
    public bool? Metered { get; set; }
    public string? EffectiveType { get; set; }
    public int? RttMs { get; set; }
    public int? DownKbps { get; set; }
}

public sealed class DeviceThermalProfile
{
    public int? StatusCode { get; set; }
}

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

public sealed class DeviceRuntimeProfile
{
    public int? Primary { get; set; }
    public string? PrimaryVersion { get; set; }
    public DeviceRuntimeEngineProfile? Engine { get; set; }
    public List<DeviceRuntimeStackEntry>? Stack { get; set; }
    public bool? AotEnabled { get; set; }
    public bool? JitEnabled { get; set; }
}

public sealed class DeviceRuntimeEngineProfile
{
    public string? Name { get; set; }
    public string? Version { get; set; }
}

public sealed class DeviceRuntimeStackEntry
{
    public int? RuntimeCode { get; set; }
    public string? Name { get; set; }
    public string? Version { get; set; }
}

public sealed class DeviceGraphicsProfile
{
    public int? RenderBackendCode { get; set; }
    public int? FpsTarget { get; set; }
    public bool? VsyncEnabled { get; set; }
}

public sealed class ConnectRequest
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required string ConfigId { get; set; }
    public required string OneTimeToken { get; set; }
    public required string AppId { get; set; }
    public required string ClientName { get; set; }
}

public sealed class ConnectResponse
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required bool Accepted { get; set; }
    public required string Reason { get; set; }
    public string? ReasonMessage { get; set; }
    public required string HostId { get; set; }
    public required string HostName { get; set; }
    public required string Message { get; set; }
    public int? WebSocketPort { get; set; }
    public string? WebSocketPath { get; set; }
    public string? WebSocketToken { get; set; }
}

public sealed class ParsedPairingDocument
{
    public required PairingConfig Config { get; init; }
    public PairingDiscoveryHint? DiscoveryHint { get; init; }
    public PairingConfig? TrustAnchorConfig { get; init; }
    public PairingConnectionHint? ConnectionHint { get; init; }
}
