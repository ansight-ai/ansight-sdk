namespace Ansight.Pairing.Models;

/// <summary>
/// Well-known action names used by the live pairing session control channel.
/// </summary>
public static class PairingControlActions
{
    public const string SessionOpen = "session.open";
    public const string SessionComplete = "session.complete";
    public const string ClientLog = "client.log";
    public const string DeviceProfile = "device.profile";
    public const string AppState = "app.state";
}
