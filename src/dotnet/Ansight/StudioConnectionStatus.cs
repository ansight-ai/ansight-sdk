namespace Ansight;

/// <summary>
/// Snapshot of the runtime-owned Studio connection state.
/// </summary>
public sealed record StudioConnectionStatus(
    bool IsRuntimeActive,
    bool IsConnected,
    HostConnectionState ConnectionState,
    bool HasCachedSession,
    bool HasSavedTicket,
    bool HasBundledTicket,
    StudioConnectionSummaryKind SummaryKind,
    string SummaryMessage);
