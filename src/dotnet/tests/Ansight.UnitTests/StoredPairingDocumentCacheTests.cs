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
}
