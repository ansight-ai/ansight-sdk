namespace Ansight;

/// <summary>
/// Snapshot of the runtime-owned host pairing state.
/// </summary>
public sealed record HostPairingStatusSnapshot(
    bool IsRuntimeActive,
    bool IsConnected,
    HostConnectionState ConnectionState,
    bool HasCachedProfile,
    bool HasPreferredProfile,
    bool HasBundledProfile,
    HostPairingSummaryKind SummaryKind,
    string SummaryMessage);
