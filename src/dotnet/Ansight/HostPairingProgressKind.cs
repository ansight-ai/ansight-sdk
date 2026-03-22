namespace Ansight;

/// <summary>
/// Categorizes structured host pairing progress updates.
/// </summary>
public enum HostPairingProgressKind
{
    Info = 0,
    Validation = 1,
    Connection = 2,
    Transport = 3,
    AppState = 4,
    Telemetry = 5,
    SessionJpegCapture = 6,
    Warning = 7
}
