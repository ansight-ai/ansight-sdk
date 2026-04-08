namespace Ansight;

/// <summary>
/// Identifies the source associated with a Studio connection status, result, or progress update.
/// </summary>
public enum StudioConnectionSource
{
    None = 0,
    AutoProbe = 1,
    CachedSession = 2,
    SavedTicket = 3,
    BundledDeveloperTicket = 4,
    BundledTicket = 5,
    Payload = 6,
    TicketReader = 7,
    HostConnection = 8,
    Transport = 9,
    Telemetry = 10,
    AppState = 11,
    SessionJpegCapture = 12
}
