namespace Ansight.Pairing;

internal static class PairingDiscoveryHintHostAddresses
{
    public static string[] Normalize(PairingDiscoveryHint? discoveryHint)
    {
        return Normalize(discoveryHint?.HostAddresses);
    }

    public static string[] Normalize(IEnumerable<string?>? hostAddresses)
    {
        var normalizedAddresses = new List<string>();
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (hostAddresses is not null)
        {
            foreach (var address in hostAddresses)
            {
                Add(address);
            }
        }

        return normalizedAddresses.ToArray();

        void Add(string? address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            var normalizedAddress = address.Trim();
            if (seenAddresses.Add(normalizedAddress))
            {
                normalizedAddresses.Add(normalizedAddress);
            }
        }
    }

    public static string? ResolvePrimary(PairingDiscoveryHint? discoveryHint)
    {
        return Normalize(discoveryHint).FirstOrDefault();
    }

    public static PairingDiscoveryHint NormalizeInPlace(PairingDiscoveryHint discoveryHint)
    {
        ArgumentNullException.ThrowIfNull(discoveryHint);

        var hostAddresses = Normalize(discoveryHint);
        discoveryHint.HostAddresses = hostAddresses.Length == 0 ? null : hostAddresses;
        return discoveryHint;
    }
}
