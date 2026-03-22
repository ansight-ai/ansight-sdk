namespace Ansight;

/// <summary>
/// Identifies the source associated with a host pairing status, result, or progress update.
/// </summary>
public enum HostPairingSource
{
    None = 0,
    AutoProbe = 1,
    CachedProfile = 2,
    StoredProfile = 3,
    BundledDeveloperProfile = 4,
    BundledProfile = 5,
    Payload = 6,
    QrConnectionPayload = 7,
    QrDiscoveryPayload = 8,
    PayloadReader = 9,
    HostConnection = 10,
    Transport = 11,
    Telemetry = 12,
    AppState = 13,
    SessionJpegCapture = 14
}
