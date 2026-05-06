using System.Security.Cryptography;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class StoredPairingDocumentCacheTests
{
    [Fact]
    public void Save_AndLoadValidated_RoundTripsPairingConfig()
    {
        using var signingKey = ECDsa.Create();
        var pairingConfig = PairingTestDocumentFactory.CreateSignedConfig(signingKey, appId: "com.ansight.cache-test");
        var configJson = PairingTestDocumentFactory.CreateConfigDocumentJson(
            pairingConfig,
            PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: "127.0.0.1"));
        var service = new PairingConfigDocumentService();
        Assert.True(service.TryParseAndValidateDocument(
            configJson,
            "com.ansight.cache-test",
            out var document,
            out var parseError), parseError);

        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"ansight-cache-{Guid.NewGuid():N}.json");
        try
        {
            var cache = new StoredPairingDocumentCache("com.ansight.cache-test", cacheFilePath);
            cache.Save(document!);

            Assert.True(cache.TryLoadValidated(
                "com.ansight.cache-test",
                out var loadedDocument,
                out var loadError), loadError);
            Assert.NotNull(loadedDocument);
            Assert.Equal(document!.Config.ConfigId, loadedDocument!.Config.ConfigId);
            Assert.Equal(document.Config.OneTimeToken, loadedDocument.Config.OneTimeToken);
            Assert.Equal(document.DiscoveryHint?.HostAddresses, loadedDocument.DiscoveryHint?.HostAddresses);
        }
        finally
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
    }

    [Fact]
    public void TryLoadValidated_WhenCachedDocumentIsExpired_ClearsTheCache()
    {
        using var signingKey = ECDsa.Create();
        var expiredConfig = PairingTestDocumentFactory.CreateSignedConfig(
            signingKey,
            appId: "com.ansight.cache-test",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var expiredJson = PairingTestDocumentFactory.CreateConfigDocumentJson(expiredConfig);

        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"ansight-cache-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(cacheFilePath, expiredJson);
            var cache = new StoredPairingDocumentCache("com.ansight.cache-test", cacheFilePath);

            Assert.False(cache.TryLoadValidated(
                "com.ansight.cache-test",
                out var loadedDocument,
                out var loadError));
            Assert.Null(loadedDocument);
            Assert.Contains("cleared", loadError, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(cacheFilePath));
        }
        finally
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
    }

    [Fact]
    public void Save_WhenDifferentWifiNetworksAreCached_RoundTripsProfilesByMostRecent()
    {
        using var signingKey = ECDsa.Create();
        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"ansight-cache-{Guid.NewGuid():N}.json");
        var now = DateTimeOffset.UtcNow;
        var homeDocument = CreateParsedDocument(
            signingKey,
            configId: "cfg-home",
            hostAddress: "10.0.0.8",
            wifiName: "Home Wi-Fi",
            capturedAt: now);
        var officeDocument = CreateParsedDocument(
            signingKey,
            configId: "cfg-office",
            hostAddress: "10.0.1.9",
            wifiName: "Office Wi-Fi",
            capturedAt: now.AddMinutes(5));

        try
        {
            var cache = new StoredPairingDocumentCache("com.ansight.cache-test", cacheFilePath);
            cache.Save(homeDocument, now);
            cache.Save(officeDocument, now.AddMinutes(5));

            Assert.True(cache.TryLoadValidatedProfiles(
                "com.ansight.cache-test",
                now.AddMinutes(5),
                out var profiles,
                out var loadError), loadError);
            Assert.Equal(2, profiles.Count);
            Assert.Equal("Office Wi-Fi", profiles[0].WifiName);
            Assert.Equal("cfg-office", profiles[0].Document.Config.ConfigId);
            Assert.Equal("Home Wi-Fi", profiles[1].WifiName);
            Assert.Equal("cfg-home", profiles[1].Document.Config.ConfigId);
        }
        finally
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
    }

    [Fact]
    public void Save_WhenSameWifiNetworkIsCached_RefreshesExistingProfile()
    {
        using var signingKey = ECDsa.Create();
        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"ansight-cache-{Guid.NewGuid():N}.json");
        var now = DateTimeOffset.UtcNow;
        var initialDocument = CreateParsedDocument(
            signingKey,
            configId: "cfg-home",
            hostAddress: "10.0.0.8",
            wifiName: "Home Wi-Fi",
            capturedAt: now);
        var refreshedDocument = CreateParsedDocument(
            signingKey,
            configId: "cfg-home-refresh",
            hostAddress: "10.0.0.42",
            wifiName: "Home Wi-Fi",
            capturedAt: now.AddMinutes(15));

        try
        {
            var cache = new StoredPairingDocumentCache("com.ansight.cache-test", cacheFilePath);
            cache.Save(initialDocument, now);
            cache.Save(refreshedDocument, now.AddMinutes(15));

            Assert.True(cache.TryLoadValidatedProfiles(
                "com.ansight.cache-test",
                now.AddMinutes(15),
                out var profiles,
                out var loadError), loadError);
            var profile = Assert.Single(profiles);
            Assert.Equal("cfg-home-refresh", profile.Document.Config.ConfigId);
            Assert.Equal(new[] { "10.0.0.42" }, profile.Document.DiscoveryHint?.HostAddresses);
            Assert.Equal(now.AddMinutes(15).Add(StoredPairingDocumentCache.DefaultProfileRetention), profile.ExpiresAtUtc);
        }
        finally
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
    }

    [Fact]
    public void TryLoadValidatedProfiles_WhenProfileRetentionExpires_RemovesProfile()
    {
        using var signingKey = ECDsa.Create();
        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"ansight-cache-{Guid.NewGuid():N}.json");
        var now = DateTimeOffset.UtcNow;
        var capturedAt = now.AddDays(-2);
        var document = CreateParsedDocument(
            signingKey,
            configId: "cfg-home",
            hostAddress: "10.0.0.8",
            wifiName: "Home Wi-Fi",
            capturedAt: capturedAt);

        try
        {
            var cache = new StoredPairingDocumentCache(
                "com.ansight.cache-test",
                cacheFilePath,
                TimeSpan.FromDays(1));
            cache.Save(document, capturedAt);

            Assert.False(cache.TryLoadValidatedProfiles(
                "com.ansight.cache-test",
                now,
                out var profiles,
                out var loadError));
            Assert.Empty(profiles);
            Assert.Contains("No cached Ansight host session", loadError, StringComparison.Ordinal);
            Assert.False(File.Exists(cacheFilePath));
        }
        finally
        {
            if (File.Exists(cacheFilePath))
            {
                File.Delete(cacheFilePath);
            }
        }
    }

    private static ParsedPairingDocument CreateParsedDocument(
        ECDsa signingKey,
        string configId,
        string hostAddress,
        string wifiName,
        DateTimeOffset capturedAt)
    {
        return new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateSignedConfig(
                signingKey,
                configId: configId,
                appId: "com.ansight.cache-test",
                expiresAt: DateTimeOffset.UtcNow.AddDays(3)),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(
                hostAddress: hostAddress,
                wifiName: wifiName,
                capturedAt: capturedAt)
        };
    }
}
