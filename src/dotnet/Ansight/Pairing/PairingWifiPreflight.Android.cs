#if ANDROID
using Android.App;
using Android.Content;
using Android.Net;

namespace Ansight.Pairing;

internal static partial class PairingWifiPreflight
{
    private static partial PairingWifiPreflightStatus GetPlatformStatusCore()
    {
        var context = Application.Context;
        if (context is null)
        {
            return PairingWifiPreflightStatus.Unknown;
        }

        var connectivityManager = context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        if (connectivityManager is null)
        {
            return PairingWifiPreflightStatus.Unknown;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            var activeNetwork = connectivityManager.ActiveNetwork;
            if (activeNetwork is null)
            {
                return PairingWifiPreflightStatus.NotConnected;
            }

            var capabilities = connectivityManager.GetNetworkCapabilities(activeNetwork);
            if (capabilities is null)
            {
                return PairingWifiPreflightStatus.Unknown;
            }

            if (capabilities.HasTransport(TransportType.Wifi) ||
                capabilities.HasTransport(TransportType.Ethernet))
            {
                return PairingWifiPreflightStatus.Connected;
            }

            if (capabilities.HasTransport(TransportType.Cellular))
            {
                return PairingWifiPreflightStatus.NotConnected;
            }

            return PairingWifiPreflightStatus.Unknown;
        }

#pragma warning disable CA1422
#pragma warning disable CS0618
        var activeNetworkInfo = connectivityManager.ActiveNetworkInfo;
        if (activeNetworkInfo is null || !activeNetworkInfo.IsConnected)
        {
            return PairingWifiPreflightStatus.NotConnected;
        }

        var status = activeNetworkInfo.Type switch
        {
            ConnectivityType.Wifi or ConnectivityType.Ethernet => PairingWifiPreflightStatus.Connected,
            ConnectivityType.Mobile => PairingWifiPreflightStatus.NotConnected,
            _ => PairingWifiPreflightStatus.Unknown
        };
#pragma warning restore CA1422
#pragma warning restore CS0618
        return status;
    }
}
#endif
