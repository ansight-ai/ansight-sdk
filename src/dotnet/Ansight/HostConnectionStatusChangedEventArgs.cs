namespace Ansight;

/// <summary>
/// Raised when the runtime-owned Ansight host connection state changes.
/// </summary>
public sealed class HostConnectionStatusChangedEventArgs : EventArgs
{
    public HostConnectionStatusChangedEventArgs(
        HostConnectionState state,
        bool isConnected,
        bool hasCachedProfile,
        string statusSummary)
    {
        State = state;
        IsConnected = isConnected;
        HasCachedProfile = hasCachedProfile;
        StatusSummary = statusSummary ?? string.Empty;
    }

    public HostConnectionState State { get; }

    public bool IsConnected { get; }

    public bool HasCachedProfile { get; }

    public string StatusSummary { get; }
}
