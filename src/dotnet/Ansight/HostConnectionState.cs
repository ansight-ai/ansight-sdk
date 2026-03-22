namespace Ansight;

/// <summary>
/// Describes the current state of the runtime-owned Ansight host connection.
/// </summary>
public enum HostConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Connected = 2
}
