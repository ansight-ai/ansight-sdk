#if !ANDROID && !IOS && !MACCATALYST
namespace Ansight.Pairing;

internal static partial class PairingWifiPreflight
{
    private static partial PairingWifiPreflightStatus GetPlatformStatusCore()
    {
        return PairingWifiPreflightStatus.Unknown;
    }
}
#endif
