using System.Security.Cryptography;
using Ansight.Pairing;

namespace Ansight.UnitTests;

internal static class PairingTestDocumentFactory
{
    public static PairingConfig CreateEnrollmentInvite(
        string configId = "invite-1",
        string appId = "com.ansight.test",
        string appName = "Ansight Test",
        string? hostId = "host-1",
        string? hostName = "test-host",
        int discoveryPort = 41000,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? expiresAt = null,
        DateTimeOffset? registrationExpiresAt = null)
    {
        var issued = issuedAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);
        var expires = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10);
        return new PairingConfig
        {
            Schema = PairingConfig.SchemaName,
            ConfigId = configId,
            AppId = appId,
            AppName = appName,
            IssuedAt = issued,
            ExpiresAt = expires,
            MinProtocolVersion = 2,
            AllowedTransports = ["ws"],
            Host = new PairingHost
            {
                HostId = hostId,
                HostName = hostName,
                DiscoveryPort = discoveryPort
            },
            Enrollment = new PairingEnrollment
            {
                Secret = PairingCrypto.ToBase64Url(RandomNumberGenerator.GetBytes(32)),
                ExpiresAt = expires,
                GrantExpiresAt = registrationExpiresAt ?? DateTimeOffset.UtcNow.AddDays(14),
                MaxUses = 1,
                MaxScopes = ["Read"],
                AllowCritical = false
            }
        };
    }

    public static PairingConfigDocument CreateConfigDocument(
        PairingConfig? config = null,
        PairingDiscoveryHint? discoveryHint = null)
    {
        config ??= CreateEnrollmentInvite();
        return new PairingConfigDocument
        {
            Schema = PairingConfigDocument.SchemaName,
            Config = config,
            Discovery = discoveryHint ?? CreateDiscoveryHint()
        };
    }

    public static PairingDiscoveryHint CreateDiscoveryHint(
        string hostAddress = "127.0.0.1",
        string[]? hostAddresses = null,
        int? discoveryPort = null,
        string? hostName = "test-host",
        string? wifiName = null,
        string? source = "unit-test",
        DateTimeOffset? capturedAt = null)
    {
        var resolvedHostAddresses = hostAddresses ?? [hostAddress];
        return new PairingDiscoveryHint
        {
            Schema = PairingDiscoveryHint.SchemaName,
            Source = source,
            HostAddresses = resolvedHostAddresses,
            DiscoveryPort = discoveryPort,
            HostName = hostName,
            WifiName = wifiName,
            CapturedAt = capturedAt ?? DateTimeOffset.UtcNow
        };
    }

    public static string CreateConfigDocumentJson(
        PairingConfig? config = null,
        PairingDiscoveryHint? discoveryHint = null)
        => PairingConfigDocumentJson.Serialize(CreateConfigDocument(config, discoveryHint));

    public static string CreateCompactConfigDocument(
        PairingConfig? config = null,
        PairingDiscoveryHint? discoveryHint = null)
        => PairingConfigCodeGenerator.Serialize(CreateConfigDocument(config, discoveryHint));
}
