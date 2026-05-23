namespace Ansight;

/// <summary>
/// Identifies the source associated with a host connection status, result, or progress update.
/// </summary>
public enum HostConnectionSource
{
    None = 0,
    AutoProbe = 1,
    CachedSession = 2,
    SavedConfig = 3,
    BundledDeveloperConfig = 4,
    BundledConfig = 5,
    Payload = 6,
    ConfigReader = 7,
    HostConnection = 8,
    Transport = 9,
    Telemetry = 10,
    AppState = 11,
    SessionJpegCapture = 12,
    TouchCapture = 13
}
