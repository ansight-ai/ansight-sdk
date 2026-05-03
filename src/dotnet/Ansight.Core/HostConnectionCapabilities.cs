namespace Ansight;

/// <summary>
/// Describes which runtime-owned host connection flows are currently available.
/// </summary>
public sealed record HostConnectionCapabilities(
    bool CanConnectUsingSavedConfig,
    bool CanConnectUsingBundledConfig,
    bool CanChooseConfigFile,
    bool CanScanConfigQrCode,
    bool CanClearSavedConfigs);
