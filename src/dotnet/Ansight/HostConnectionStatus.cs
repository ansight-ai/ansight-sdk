namespace Ansight;

/// <summary>
/// Snapshot of the runtime-owned host connection state.
/// </summary>
public sealed record HostConnectionStatus(
    bool IsRuntimeActive,
    bool IsConnected,
    HostConnectionState ConnectionState,
    bool HasCachedSession,
    bool HasSavedConfig,
    bool HasBundledConfig,
    HostConnectionSummaryKind SummaryKind,
    string SummaryMessage);
