namespace Ansight;

/// <summary>
/// Describes which runtime-owned host pairing actions are currently available.
/// </summary>
public sealed record HostPairingCapabilities(
    bool CanConnectUsingStored,
    bool CanConnectUsingBundled,
    bool CanClearProfiles,
    bool CanUseQrPayloadWithBaseProfile);
