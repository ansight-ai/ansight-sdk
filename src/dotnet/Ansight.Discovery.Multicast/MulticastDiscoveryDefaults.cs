using System.Net;

namespace Ansight.Discovery.Multicast;

public static class MulticastDiscoveryDefaults
{
    public const int DiscoveryPort = 45123;
    public const string MulticastGroup = "239.255.43.21";

    public static IPAddress MulticastAddress => IPAddress.Parse(MulticastGroup);
}
