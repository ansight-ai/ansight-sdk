using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class HostPairingManagerTests
{
    [Fact]
    public async Task AutoConnectAsync_WhenCachedProfileConnects_DoesNotTouchStoredOrBundledProfiles()
    {
        var preferredProfilePath = CreateTempFilePath();
        var bundledLoaderCallCount = 0;
        using var hostConnection = new FakeHostConnection();
        hostConnection.HasCachedProfile = true;
        hostConnection.CachedConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using cached profile."));
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new HostPairingOptions
            {
                BundledProfileLoader = _ =>
                {
                    bundledLoaderCallCount++;
                    return Task.FromResult<string?>(null);
                }
            });

        var result = await manager.AutoConnectAsync();

        Assert.True(result.Success);
        Assert.Equal(1, hostConnection.CachedConnectCallCount);
        Assert.Empty(hostConnection.ConnectDocuments);
        Assert.Equal(0, bundledLoaderCallCount);
    }

    [Fact]
    public async Task ConnectFromPayloadAsync_WhenQrConnectionPayloadIsProvided_MergesWithStoredPreferredProfile()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var preferredDocument = CreateDocument(signingKey, configId: "cfg-base", hostAddress: "192.168.1.50");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult());
        using var manager = CreateManager(hostConnection, preferredProfilePath);
        SavePreferredProfile(preferredProfilePath, preferredDocument);

        var payload = JsonSerializer.Serialize(
            PairingTestDocumentFactory.CreateQrConnectionPayload(
                connectionHint: PairingTestDocumentFactory.CreateConnectionHint(
                    configId: "cfg-override",
                    oneTimeToken: "token-override",
                    challengePubKey: "challenge-override"),
                discoveryHint: PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: "10.0.0.25")),
            PairingJson.Compact);

        var result = await manager.ConnectFromPayloadAsync(payload, "QR pairing code");

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-override", connectedDocument.Config.ConfigId);
        Assert.Equal("token-override", connectedDocument.Config.OneTimeToken);
        Assert.Equal("challenge-override", connectedDocument.Config.Challenge.ChallengePubKey);
        Assert.Equal("cfg-base", connectedDocument.TrustAnchorConfig?.ConfigId);
        Assert.Equal("10.0.0.25", connectedDocument.DiscoveryHint?.HostAddress);
    }

    [Fact]
    public async Task ConnectUsingStoredProfileAsync_WhenStoredProfileIsRejectedWithResetCode_RetriesBundledProfile()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var preferredDocument = CreateDocument(signingKey, configId: "cfg-preferred", hostAddress: "192.168.1.10");
        var bundledDocument = CreateDocument(signingKey, configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateRejectedConnectionResult("PairingTokenExpired", "Saved token expired."));
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled profile."));
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new HostPairingOptions
            {
                BundledProfileLoader = _ => Task.FromResult<string?>(PairingDocumentJson.Serialize(bundledDocument))
            });
        SavePreferredProfile(preferredProfilePath, preferredDocument);

        var result = await manager.ConnectUsingStoredProfileAsync();

        Assert.True(result.Success);
        Assert.Equal(2, hostConnection.ConnectDocuments.Count);
        Assert.Equal("cfg-preferred", hostConnection.ConnectDocuments[0].Config.ConfigId);
        Assert.Equal("cfg-bundled", hostConnection.ConnectDocuments[1].Config.ConfigId);
        Assert.True(manager.HasPreferredProfile);
    }

    [Fact]
    public async Task ConnectUsingStoredProfileAsync_WhenNoStoredProfileExists_DoesNotFallbackToBundledProfile()
    {
        var preferredProfilePath = CreateTempFilePath();
        var bundledLoaderCallCount = 0;
        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new HostPairingOptions
            {
                BundledProfileLoader = _ =>
                {
                    bundledLoaderCallCount++;
                    return Task.FromResult<string?>(null);
                }
            });

        var result = await manager.ConnectUsingStoredProfileAsync();

        Assert.False(result.Success);
        Assert.Contains("No saved Ansight pairing profile is available.", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, bundledLoaderCallCount);
    }

    [Fact]
    public async Task AutoConnectAsync_WhenStoredProfileIsMissing_FallsBackToBundledProfile()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundledDocument = CreateDocument(signingKey, configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled profile."));
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new HostPairingOptions
            {
                BundledProfileLoader = _ => Task.FromResult<string?>(PairingDocumentJson.Serialize(bundledDocument))
            });

        var result = await manager.AutoConnectAsync();

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public void ClearStoredProfiles_ClearsPreferredStoreAndCachedHostProfile()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var preferredDocument = CreateDocument(signingKey, configId: "cfg-preferred", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        hostConnection.HasCachedProfile = true;
        using var manager = CreateManager(hostConnection, preferredProfilePath);
        SavePreferredProfile(preferredProfilePath, preferredDocument);

        var result = manager.ClearStoredProfiles();

        Assert.True(result.Success);
        Assert.False(manager.HasPreferredProfile);
        Assert.Equal(1, hostConnection.ClearCachedProfileCallCount);
        Assert.False(hostConnection.HasCachedProfile);
    }

    private static HostPairingManager CreateManager(
        FakeHostConnection hostConnection,
        string preferredProfilePath,
        HostPairingOptions? options = null)
    {
        var configuredOptions = options ?? new HostPairingOptions();
        configuredOptions.PreferredProfilePath = preferredProfilePath;

        return new HostPairingManager(
            hostConnection,
            configuredOptions,
            new StoredHostPairingProfileStore("unit-test", preferredProfilePath));
    }

    private static ParsedPairingDocument CreateDocument(
        ECDsa signingKey,
        string configId,
        string hostAddress)
    {
        return new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateSignedConfig(signingKey, configId: configId),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: hostAddress)
        };
    }

    private static void SavePreferredProfile(string preferredProfilePath, ParsedPairingDocument document)
    {
        var store = new StoredHostPairingProfileStore("unit-test", preferredProfilePath);
        store.Save(document);
    }

    private static string CreateTempFilePath()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "Ansight.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return Path.Combine(directoryPath, "preferred-profile.json");
    }

    private static HostConnectionActionResult CreateSuccessConnectionResult(string message = "Connected to the Ansight host.")
    {
        return HostConnectionActionResult.FromSuccess(
            message,
            OpenSessionResult.FromSuccess(
                "connected",
                IPAddress.Loopback,
                new ConnectResponse
                {
                    Type = "CONNECT_RESP",
                    Ver = 1,
                    Accepted = true,
                    Reason = "ok",
                    HostId = "host-1",
                    HostName = "Studio",
                    Message = "ready",
                    WebSocketPort = 45124,
                    WebSocketPath = "/ws",
                    WebSocketToken = "token"
                },
                "HOST_HELLO"));
    }

    private static HostConnectionActionResult CreateRejectedConnectionResult(string rejectionCode, string rejectionMessage)
    {
        return HostConnectionActionResult.FromFailure(
            rejectionMessage,
            OpenSessionResult.FromRejected(
                IPAddress.Loopback,
                new ConnectResponse
                {
                    Type = "CONNECT_RESP",
                    Ver = 1,
                    Accepted = false,
                    Reason = rejectionCode,
                    ReasonMessage = rejectionMessage,
                    HostId = "host-1",
                    HostName = "Studio",
                    Message = rejectionMessage
                }));
    }

    private sealed class FakeHostConnection : IHostConnection, IDisposable
    {
        private readonly PairingConfigDocumentService documentService = new();

        public HostConnectionState State { get; private set; } = HostConnectionState.Disconnected;

        public bool IsConnected { get; private set; }

        public bool HasCachedProfile { get; set; }

        public string StatusSummary { get; private set; } = "No Ansight host session is connected.";

        public List<ParsedPairingDocument> ConnectDocuments { get; } = new();

        public Queue<HostConnectionActionResult> ConnectResults { get; } = new();

        public Queue<HostConnectionActionResult> CachedConnectResults { get; } = new();

        public int CachedConnectCallCount { get; private set; }

        public int ClearCachedProfileCallCount { get; private set; }

        public event EventHandler<HostConnectionStatusChangedEventArgs>? StatusChanged
        {
            add { }
            remove { }
        }

        public bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error)
        {
            return documentService.TryParseAndValidateDocument(configJson, "com.ansight.test", out document, out error);
        }

        public Task<HostConnectionActionResult> ConnectAsync(
            ParsedPairingDocument document,
            string? clientName = null,
            PairingConnectionOptions? connectionOptions = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ConnectDocuments.Add(document);
            var result = ConnectResults.Count > 0
                ? ConnectResults.Dequeue()
                : CreateSuccessConnectionResult();
            ApplyState(result);
            return Task.FromResult(result);
        }

        public Task<HostConnectionActionResult> ConnectUsingCachedProfileAsync(
            string? clientName = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CachedConnectCallCount++;
            var result = CachedConnectResults.Count > 0
                ? CachedConnectResults.Dequeue()
                : HostConnectionActionResult.FromFailure("No cached profile.");
            ApplyState(result);
            return Task.FromResult(result);
        }

        public Task<HostConnectionActionResult> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            State = HostConnectionState.Disconnected;
            StatusSummary = "No Ansight host session is connected.";
            return Task.FromResult(HostConnectionActionResult.FromSuccess("Disconnected."));
        }

        public HostConnectionActionResult ClearCachedProfile()
        {
            ClearCachedProfileCallCount++;
            HasCachedProfile = false;
            return HostConnectionActionResult.FromSuccess("Cleared the cached Ansight host pairing profile.");
        }

        public void Dispose()
        {
        }

        private void ApplyState(HostConnectionActionResult result)
        {
            IsConnected = result.Success;
            State = result.Success ? HostConnectionState.Connected : HostConnectionState.Disconnected;
            StatusSummary = result.Message;
        }
    }
}
