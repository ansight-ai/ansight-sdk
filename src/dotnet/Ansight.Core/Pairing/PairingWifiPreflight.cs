namespace Ansight.Pairing;

internal enum PairingWifiPreflightStatus
{
    Unknown = 0,
    Connected = 1,
    NotConnected = 2,
    Cellular = 3
}

internal static partial class PairingWifiPreflight
{
    public static PairingWifiPreflightStatus GetStatus()
    {
        try
        {
            return GetPlatformStatusCore();
        }
        catch (Exception ex)
        {
            global::Ansight.Logger.Info($"Wi-Fi preflight could not determine current network status: {ex.Message}");
            return PairingWifiPreflightStatus.Unknown;
        }
    }

    private static partial PairingWifiPreflightStatus GetPlatformStatusCore();
}
