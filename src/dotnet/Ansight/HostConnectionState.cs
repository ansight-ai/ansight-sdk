namespace Ansight;

/// <summary>
/// Describes the current state of the runtime-owned Ansight host connection.
/// </summary>
public enum HostConnectionState
{
    /// <summary>
    /// No live Ansight host session is currently connected.
    /// </summary>
    Disconnected = 0,

    /// <summary>
    /// A connection attempt is in progress.
    /// </summary>
    Connecting = 1,

    /// <summary>
    /// A live Ansight host session is currently connected.
    /// </summary>
    Connected = 2
}
