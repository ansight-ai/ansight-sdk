namespace Ansight;

/// <summary>
/// Identifies the host pairing action that produced a result.
/// </summary>
public enum HostPairingActionKind
{
    None = 0,
    Connect = 1,
    AutoConnect = 2,
    ConnectUsingStoredProfile = 3,
    ConnectUsingBundledProfile = 4,
    ConnectFromPayload = 5,
    Disconnect = 6,
    ClearStoredProfiles = 7,
    ConnectUsingCachedProfile = 8
}
