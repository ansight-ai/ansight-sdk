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
        Assert.Equal(new[] { "10.0.0.25" }, connectedDocument.DiscoveryHint?.HostAddresses);
    }

    [Fact]
    public async Task ConnectUsingStoredProfileAsync_WhenStoredProfileNeedsAFreshHostAddress_FallsBackToBundledProfile()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var preferredDocument = CreateDocument(signingKey, configId: "cfg-preferred", hostAddress: "192.168.1.10");
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
        SavePreferredProfile(preferredProfilePath, preferredDocument);

        var result = await manager.ConnectUsingStoredProfileAsync();

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled", connectedDocument.Config.ConfigId);
        Assert.True(manager.HasPreferredProfile);
    }

    [Fact]
    public async Task ConnectUsingStoredProfileAsync_WhenStoredProfileContainsRememberedHostAddress_RewritesItWithoutTheAddress()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var preferredDocument = CreateDocument(signingKey, configId: "cfg-preferred", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(hostConnection, preferredProfilePath);
        SavePreferredProfile(preferredProfilePath, preferredDocument);

        var result = await manager.ConnectUsingStoredProfileAsync();

        Assert.False(result.Success);
        Assert.Equal(PairingFailureCodes.HostAddressRequired, result.ReasonCode);
        Assert.Empty(hostConnection.ConnectDocuments);

        var store = new StoredHostPairingProfileStore("unit-test", preferredProfilePath);
        Assert.True(store.TryLoad(out var storedJson, out var error), error);

        var documentService = new PairingConfigDocumentService();
        Assert.True(documentService.TryParseAndValidateDocument(storedJson!, "com.ansight.test", out var storedDocument, out error), error);
        Assert.NotNull(storedDocument);
        Assert.NotNull(storedDocument!.DiscoveryHint);
        Assert.Null(storedDocument.DiscoveryHint.HostAddresses);
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
    public async Task AutoConnectAsync_WhenBundledProfileAssemblyContainsEmbeddedResources_PrefersDeveloperResourceLogicalName()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundledDeveloperDocument = CreateDocument(signingKey, configId: "cfg-developer", hostAddress: "192.168.1.30");
        var bundledDocument = CreateDocument(signingKey, configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection
        {
            ParseDocumentOverride = configJson => configJson.Trim() switch
            {
                "developer-resource" => bundledDeveloperDocument,
                "bundled-resource" => bundledDocument,
                _ => null
            }
        };
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled profile."));
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new HostPairingOptions
            {
                BundledProfileAssembly = typeof(HostPairingManagerTests).Assembly
            });

        var result = await manager.AutoConnectAsync();

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-developer", connectedDocument.Config.ConfigId);
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

    [Fact]
    public async Task RefreshCapabilitiesAsync_WhenBundledProfileProbeSucceeds_UpdatesStatusSnapshot()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundledDocument = CreateDocument(signingKey, configId: "cfg-bundled", hostAddress: "192.168.1.20");
        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new HostPairingOptions
            {
                BundledProfileLoader = _ => Task.FromResult<string?>(PairingDocumentJson.Serialize(bundledDocument))
            });

        var capabilities = await manager.RefreshCapabilitiesAsync();

        Assert.True(capabilities.CanConnectUsingBundled);
        Assert.True(manager.Status.HasBundledProfile);
        Assert.Equal(HostPairingSummaryKind.DisconnectedBundledProfileAvailable, manager.Status.SummaryKind);
    }

    [Fact]
    public void Status_WhenStoredAndCachedProfilesExist_ReportsMultipleProfilesAvailable()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var preferredDocument = CreateDocument(signingKey, configId: "cfg-preferred", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        hostConnection.HasCachedProfile = true;
        SavePreferredProfile(preferredProfilePath, preferredDocument);
        using var manager = CreateManager(hostConnection, preferredProfilePath);

        Assert.Equal(HostPairingSummaryKind.DisconnectedMultipleProfilesAvailable, manager.Status.SummaryKind);
        Assert.True(manager.Capabilities.CanConnectUsingStored);
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
            new StoredHostPairingProfileStore("unit-test", preferredProfilePath),
            isRuntimeActive: () => true);
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

        public Func<string, ParsedPairingDocument?>? ParseDocumentOverride { get; set; }

        public event EventHandler<HostConnectionStatusChangedEventArgs>? StatusChanged;

        public bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error)
        {
            if (ParseDocumentOverride is not null)
            {
                document = ParseDocumentOverride(configJson);
                error = document is null ? "Invalid pairing document." : string.Empty;
                return document is not null;
            }

            return documentService.TryParseAndValidateDocument(configJson, "com.ansight.test", out document, out error);
        }

        public Task<HostConnectionActionResult> ConnectAsync(
            ParsedPairingDocument document,
            string? clientName = null,
            PairingConnectionOptions? connectionOptions = null,
            IProgress<HostPairingProgressUpdate>? progress = null,
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
            IProgress<HostPairingProgressUpdate>? progress = null,
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
            RaiseStatusChanged();
            return Task.FromResult(HostConnectionActionResult.FromSuccess("Disconnected."));
        }

        public HostConnectionActionResult ClearCachedProfile()
        {
            ClearCachedProfileCallCount++;
            HasCachedProfile = false;
            RaiseStatusChanged();
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
            RaiseStatusChanged();
        }

        private void RaiseStatusChanged()
        {
            StatusChanged?.Invoke(this, new HostConnectionStatusChangedEventArgs(
                State,
                IsConnected,
                HasCachedProfile,
                StatusSummary));
        }
    }
}
