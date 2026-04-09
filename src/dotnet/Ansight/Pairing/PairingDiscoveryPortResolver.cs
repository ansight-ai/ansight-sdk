namespace Ansight.Pairing;

internal static class PairingDiscoveryPortResolver
{
    public static int Resolve(ParsedPairingDocument document, int? configuredDiscoveryPort = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var candidates = new[]
        {
            configuredDiscoveryPort,
            document.DiscoveryHint?.DiscoveryPort,
            document.Config.Host.DiscoveryPort
        };

        foreach (var candidate in candidates)
        {
            if (candidate is > 0 and <= ushort.MaxValue)
            {
                return candidate.Value;
            }
        }

        return PairingProtocolDefaults.DiscoveryPort;
    }
}
