using Ansight.Pairing.Models;

namespace Ansight.Pairing;

internal static class LocalPairingDocumentFactory
{
    public static ParsedPairingDocument Create(
        string appId,
        string appName,
        string hostAddress,
        int discoveryPort)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddDays(3650);
        return new ParsedPairingDocument
        {
            Config = new PairingConfig
            {
                Schema = PairingConfig.SchemaName,
                ConfigId = $"{PairingEnrollmentModes.LocalConfigPrefix}{appId}",
                AppId = appId,
                AppName = appName,
                IssuedAt = now,
                ExpiresAt = expiresAt,
                MinProtocolVersion = 2,
                AllowedTransports = ["ws"],
                Host = new PairingHost
                {
                    HostName = "Local Ansight Studio",
                    DiscoveryPort = discoveryPort
                },
                Enrollment = new PairingEnrollment
                {
                    Secret = PairingDeviceIdentity.GetOrCreateAccessToken(appId),
                    ExpiresAt = expiresAt,
                    GrantExpiresAt = expiresAt,
                    MaxUses = 1,
                    MaxScopes = ["Read"],
                    AllowCritical = false
                }
            },
            DiscoveryHint = new PairingDiscoveryHint
            {
                Schema = PairingDiscoveryHint.SchemaName,
                Source = "runtime-local",
                HostAddresses = [hostAddress],
                DiscoveryPort = discoveryPort,
                HostName = "Local Ansight Studio",
                CapturedAt = now
            }
        };
    }
}
