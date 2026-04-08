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
            new StudioConnectionOptions
            {
                BundledTicketLoader = _ =>
                {
                    bundledLoaderCallCount++;
                    return Task.FromResult<string?>(null);
                }
            });

        var result = await manager.ConnectAsync(StudioConnectionRequest.Auto());

        Assert.True(result.Success);
        Assert.Equal(1, hostConnection.CachedConnectCallCount);
        Assert.Empty(hostConnection.ConnectDocuments);
        Assert.Equal(0, bundledLoaderCallCount);
    }

    [Fact]
    public async Task ConnectFromPayloadAsync_WhenPairingTicketIsProvided_ConnectsThatTicket()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var preferredDocument = CreateDocument(signingKey, configId: "cfg-base", hostAddress: "192.168.1.50");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult());
        using var manager = CreateManager(hostConnection, preferredProfilePath);
        SavePreferredProfile(preferredProfilePath, preferredDocument);

        var payload = PairingTicketJson.Serialize(
            new Ansight.Pairing.Models.PairingTicket
            {
                Config = PairingTestDocumentFactory.CreateSignedConfig(
                    signingKey,
                    configId: "cfg-override",
                    oneTimeToken: "token-override",
                    challengePubKey: "challenge-override"),
                Discovery = PairingTestDocumentFactory.CreateDiscoveryHint(
                    hostAddress: "10.0.0.25",
                    discoveryPort: 45200)
            });

        var result = await manager.ConnectAsync(StudioConnectionRequest.PayloadText(payload, "pairing ticket"));

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-override", connectedDocument.Config.ConfigId);
        Assert.Equal("token-override", connectedDocument.Config.OneTimeToken);
        Assert.Equal("challenge-override", connectedDocument.Config.Challenge.ChallengePubKey);
        Assert.Equal(new[] { "10.0.0.25" }, connectedDocument.DiscoveryHint?.HostAddresses);
        Assert.Equal(45200, hostConnection.LastConnectionOptions?.DiscoveryPort);
    }

    [Fact]
    public async Task ConnectUsingBundledProfileAsync_WhenDiscoveryPortOverrideIsConfigured_PassesItToTheConnection()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundledDocument = CreateDocument(signingKey, configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled profile."));
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new StudioConnectionOptions
            {
                DiscoveryPort = 45200,
                BundledTicketLoader = _ => Task.FromResult<string?>(CreateTicketJson(bundledDocument))
            });

        var result = await manager.ConnectAsync(StudioConnectionRequest.BundledTicket());

        Assert.True(result.Success);
        Assert.Equal(45200, hostConnection.LastConnectionOptions?.DiscoveryPort);
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
            new StudioConnectionOptions
            {
                BundledTicketLoader = _ => Task.FromResult<string?>(CreateTicketJson(bundledDocument))
            });
        SavePreferredProfile(preferredProfilePath, preferredDocument);

        var result = await manager.ConnectAsync(StudioConnectionRequest.SavedTicket());

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled", connectedDocument.Config.ConfigId);
        Assert.True(manager.HasSavedTicket);
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

        var result = await manager.ConnectAsync(StudioConnectionRequest.SavedTicket());

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
            new StudioConnectionOptions
            {
                BundledTicketLoader = _ =>
                {
                    bundledLoaderCallCount++;
                    return Task.FromResult<string?>(null);
                }
            });

        var result = await manager.ConnectAsync(StudioConnectionRequest.SavedTicket());

        Assert.False(result.Success);
        Assert.Contains("No saved Ansight pairing ticket is available.", result.Message, StringComparison.Ordinal);
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
            new StudioConnectionOptions
            {
                BundledTicketLoader = _ => Task.FromResult<string?>(CreateTicketJson(bundledDocument))
            });

        var result = await manager.ConnectAsync(StudioConnectionRequest.Auto());

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public async Task AutoConnectAsync_WhenBundledTicketAssemblyContainsEmbeddedResources_PrefersDeveloperResourceLogicalName()
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
            new StudioConnectionOptions
            {
                BundledTicketAssembly = typeof(HostPairingManagerTests).Assembly
            });

        var result = await manager.ConnectAsync(StudioConnectionRequest.Auto());

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-developer", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public async Task HandleRuntimeActivatedAsync_WhenBundledDeveloperProfileExists_AttemptsAutoConnect()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundledDeveloperDocument = CreateDocument(signingKey, configId: "cfg-developer", hostAddress: "192.168.1.30");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled developer profile."));
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new StudioConnectionOptions
            {
                BundledDeveloperTicketLoader = _ => Task.FromResult<string?>(CreateTicketJson(bundledDeveloperDocument))
            });

        await manager.HandleRuntimeActivatedAsync();

        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-developer", connectedDocument.Config.ConfigId);
        Assert.True(manager.Status.HasBundledTicket);
    }

    [Fact]
    public async Task HandleRuntimeActivatedAsync_WhenOnlyBundledProfileExists_DoesNotAttemptAutoConnect()
    {
        var preferredProfilePath = CreateTempFilePath();
        using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bundledDocument = CreateDocument(signingKey, configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled profile."));
        using var manager = CreateManager(
            hostConnection,
            preferredProfilePath,
            new StudioConnectionOptions
            {
                BundledTicketLoader = _ => Task.FromResult<string?>(CreateTicketJson(bundledDocument))
            });

        await manager.HandleRuntimeActivatedAsync();

        Assert.Empty(hostConnection.ConnectDocuments);
        Assert.False(hostConnection.IsConnected);
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

        var result = manager.ClearSavedTickets();

        Assert.True(result.Success);
        Assert.False(manager.HasSavedTicket);
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
            new StudioConnectionOptions
            {
                BundledTicketLoader = _ => Task.FromResult<string?>(CreateTicketJson(bundledDocument))
            });

        var capabilities = await manager.RefreshCapabilitiesAsync();

        Assert.True(capabilities.CanConnectUsingBundledTicket);
        Assert.True(manager.Status.HasBundledTicket);
        Assert.Equal(StudioConnectionSummaryKind.DisconnectedBundledTicketAvailable, manager.Status.SummaryKind);
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

        Assert.Equal(StudioConnectionSummaryKind.DisconnectedMultipleTicketsAvailable, manager.Status.SummaryKind);
        Assert.True(manager.Capabilities.CanConnectUsingSavedTicket);
    }

    private static HostPairingManager CreateManager(
        FakeHostConnection hostConnection,
        string preferredProfilePath,
        StudioConnectionOptions? options = null)
    {
        var configuredOptions = options ?? new StudioConnectionOptions();
        configuredOptions.SavedTicketPath = preferredProfilePath;

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

    private static string CreateTicketJson(ParsedPairingDocument document)
    {
        return PairingTicketJson.Serialize(
            new Ansight.Pairing.Models.PairingTicket
            {
                Config = document.Config,
                Discovery = document.DiscoveryHint
            });
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
                }));
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

        public PairingConnectionOptions? LastConnectionOptions { get; private set; }

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
            IProgress<StudioConnectionProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ConnectDocuments.Add(document);
            LastConnectionOptions = connectionOptions;
            var result = ConnectResults.Count > 0
                ? ConnectResults.Dequeue()
                : CreateSuccessConnectionResult();
            ApplyState(result);
            return Task.FromResult(result);
        }

        public Task<HostConnectionActionResult> ConnectUsingCachedProfileAsync(
            string? clientName = null,
            IProgress<StudioConnectionProgressUpdate>? progress = null,
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
            return HostConnectionActionResult.FromSuccess("Cleared the cached Ansight Studio session.");
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
