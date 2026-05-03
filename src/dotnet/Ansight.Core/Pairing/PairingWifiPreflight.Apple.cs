#if IOS || MACCATALYST
using System.Net;
using SystemConfiguration;

namespace Ansight.Pairing;

internal static partial class PairingWifiPreflight
{
    private static partial PairingWifiPreflightStatus GetPlatformStatusCore()
    {
        // Reachability remains the simplest synchronous transport hint we can query
        // before attempting the UDP bootstrap.
#pragma warning disable CA1422
        using var reachability = new NetworkReachability(IPAddress.Any);
        if (!reachability.TryGetFlags(out var flags))
        {
            return PairingWifiPreflightStatus.Unknown;
        }
#pragma warning restore CA1422

        var reachable = flags.HasFlag(NetworkReachabilityFlags.Reachable);
        var connectionRequired = flags.HasFlag(NetworkReachabilityFlags.ConnectionRequired);
        var interventionRequired = flags.HasFlag(NetworkReachabilityFlags.InterventionRequired);
        if (!reachable || connectionRequired || interventionRequired)
        {
            return PairingWifiPreflightStatus.NotConnected;
        }

#if IOS
        if (flags.HasFlag(NetworkReachabilityFlags.IsWWAN))
        {
            return PairingWifiPreflightStatus.NotConnected;
        }
#endif

        return PairingWifiPreflightStatus.Connected;
    }
}
#endif
