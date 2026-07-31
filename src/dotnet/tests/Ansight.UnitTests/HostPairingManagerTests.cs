using System.Net;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class HostPairingManagerTests
{
    [Fact]
    public async Task AutoConnectAsync_WhenCachedProfileConnects_DoesNotTouchSavedOrBundledConfigs()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledLoaderCallCount = 0;
        using var hostConnection = new FakeHostConnection();
        hostConnection.HasCachedProfile = true;
        hostConnection.CachedConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using cached profile."));
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ =>
                {
                    bundledLoaderCallCount++;
                    return Task.FromResult<string?>(null);
                }
            });

        var result = await manager.ConnectAsync(HostConnectionRequest.Auto());

        Assert.True(result.Success);
        Assert.Equal(1, hostConnection.CachedConnectCallCount);
        Assert.Empty(hostConnection.ConnectDocuments);
        Assert.Equal(0, bundledLoaderCallCount);
    }

    [Fact]
    public async Task ConnectFromPayloadAsync_WhenPairingConfigIsProvided_ConnectsThatConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-base", hostAddress: "192.168.1.50");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult());
        using var manager = CreateManager(hostConnection, savedConfigPath);
        SaveSavedConfig(savedConfigPath, savedDocument);

        var payload = PairingConfigDocumentJson.Serialize(
            PairingTestDocumentFactory.CreateConfigDocument(
                PairingTestDocumentFactory.CreateEnrollmentInvite(
                    configId: "cfg-override"),
                PairingTestDocumentFactory.CreateDiscoveryHint(
                    hostAddress: "10.0.0.25",
                    discoveryPort: 45200)));

        var result = await manager.ConnectAsync(HostConnectionRequest.PayloadText(payload, "pairing config"));

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-override", connectedDocument.Config.ConfigId);
        Assert.False(string.IsNullOrWhiteSpace(connectedDocument.Config.Enrollment!.Secret));
        Assert.Equal(new[] { "10.0.0.25" }, connectedDocument.DiscoveryHint?.HostAddresses);
        Assert.Equal(45200, hostConnection.LastConnectionOptions?.DiscoveryPort);
    }

    [Fact]
    public async Task ConnectFromPayloadAsync_WhenAlreadyConnected_UsesSuppliedPayloadAsOverride()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-saved", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using saved config."));
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using supplied pairing config."));
        using var manager = CreateManager(hostConnection, savedConfigPath);
        SaveSavedConfig(savedConfigPath, savedDocument);

        var payload = PairingConfigDocumentJson.Serialize(
            PairingTestDocumentFactory.CreateConfigDocument(
                PairingTestDocumentFactory.CreateEnrollmentInvite(
                    configId: "cfg-override"),
                PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: "10.0.0.25")));

        var initialResult = await manager.ConnectAsync(HostConnectionRequest.SavedConfig());
        var overrideResult = await manager.ConnectAsync(HostConnectionRequest.PayloadText(payload, "pairing config"));

        Assert.True(initialResult.Success);
        Assert.True(overrideResult.Success);
        Assert.Equal(2, hostConnection.ConnectDocuments.Count);
        Assert.Equal("cfg-saved", hostConnection.ConnectDocuments[0].Config.ConfigId);
        Assert.Equal("cfg-override", hostConnection.ConnectDocuments[1].Config.ConfigId);
    }

    [Fact]
    public async Task ConnectUsingBundledConfigAsync_WhenDiscoveryPortOverrideIsConfigured_PassesItToTheConnection()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled config."));
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                DiscoveryPort = 45200,
                BundledConfigLoader = _ => Task.FromResult<string?>(CreateConfigDocumentJson(bundledDocument))
            });

        var result = await manager.ConnectAsync(HostConnectionRequest.BundledConfig());

        Assert.True(result.Success);
        Assert.Equal(45200, hostConnection.LastConnectionOptions?.DiscoveryPort);
    }

    [Fact]
    public async Task ConnectUsingSavedConfigAsync_WhenSavedConfigContainsHostAddress_ConnectsUsingSavedConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-saved", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using saved config."));
        using var manager = CreateManager(hostConnection, savedConfigPath);
        SaveSavedConfig(savedConfigPath, savedDocument);

        var result = await manager.ConnectAsync(HostConnectionRequest.SavedConfig());

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-saved", connectedDocument.Config.ConfigId);
        Assert.Equal(0, hostConnection.CachedConnectCallCount);
        Assert.True(manager.HasSavedConfig);
    }

    [Fact]
    public async Task ConnectUsingSavedConfigAsync_WhenCachedProfileExists_StillUsesSavedConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-saved", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        hostConnection.HasCachedProfile = true;
        hostConnection.CachedConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using cached profile."));
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using saved config."));
        using var manager = CreateManager(hostConnection, savedConfigPath);
        SaveSavedConfig(savedConfigPath, savedDocument);

        var result = await manager.ConnectAsync(HostConnectionRequest.SavedConfig());

        Assert.True(result.Success);
        Assert.Equal(0, hostConnection.CachedConnectCallCount);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-saved", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public async Task AutoConnectAsync_WhenSavedConfigNeedsFreshHostAddress_FallsBackToBundledConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocumentWithoutHostAddress(configId: "cfg-saved");
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled config."));
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ => Task.FromResult<string?>(CreateConfigDocumentJson(bundledDocument))
            });
        SaveSavedConfig(savedConfigPath, savedDocument);

        var result = await manager.ConnectAsync(HostConnectionRequest.Auto());

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled", connectedDocument.Config.ConfigId);
        Assert.True(manager.HasSavedConfig);
    }

    [Fact]
    public async Task AutoConnectAsync_WhenSavedConfigRequiresSignIn_DoesNotFallbackOrClearSavedConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledLoaderCallCount = 0;
        var savedDocument = CreateDocument(configId: "cfg-saved", hostAddress: "192.168.1.10");
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateRejectedConnectionResult(
            PairingFailureCodes.SignInRequired,
            "Sign in required. Sign in to Ansight Studio before connecting an app."));
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ =>
                {
                    bundledLoaderCallCount++;
                    return Task.FromResult<string?>(CreateConfigDocumentJson(bundledDocument));
                }
            });
        SaveSavedConfig(savedConfigPath, savedDocument);

        var result = await manager.ConnectAsync(HostConnectionRequest.Auto());

        Assert.False(result.Success);
        Assert.Equal(PairingFailureCodes.SignInRequired, result.ReasonCode);
        Assert.Contains("Sign in required", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, bundledLoaderCallCount);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-saved", connectedDocument.Config.ConfigId);
        Assert.True(manager.HasSavedConfig);
    }

    [Fact]
    public async Task AutoConnectAsync_WhenLocalEnrollmentIsAvailable_DoesNotTouchStaleSavedConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-stale", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection
        {
            CanAttemptLocalEnrollment = true
        };
        hostConnection.ConnectResults.Enqueue(HostSessionActionResult.FromFailure(
            "Saved registration timed out.",
            source: HostConnectionSource.SavedConfig,
            reasonCode: PairingFailureCodes.UdpBootstrapTimeout));
        hostConnection.LocalEnrollmentResults.Enqueue(
            CreateSuccessConnectionResult("Connected using local enrollment."));
        using var manager = CreateManager(hostConnection, savedConfigPath);
        SaveSavedConfig(savedConfigPath, savedDocument);

        var result = await manager.ConnectAsync(HostConnectionRequest.Auto());

        Assert.True(result.Success);
        Assert.Equal(1, hostConnection.LocalEnrollmentCallCount);
        Assert.Empty(hostConnection.ConnectDocuments);
    }

    [Fact]
    public async Task ConnectFromPayloadAsync_WhenPayloadIsInvalid_DoesNotOverwriteSavedConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-saved", hostAddress: "192.168.1.10");
        var invalidPayloadDocument = CreateDocumentWithoutHostAddress(configId: "cfg-invalid");

        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(hostConnection, savedConfigPath);
        SaveSavedConfig(savedConfigPath, savedDocument);

        var result = await manager.ConnectAsync(
            HostConnectionRequest.PayloadText(CreateConfigDocumentJson(invalidPayloadDocument), "pairing config"));

        Assert.False(result.Success);
        Assert.Equal(PairingFailureCodes.HostAddressRequired, result.ReasonCode);
        Assert.Empty(hostConnection.ConnectDocuments);

        var store = new StoredHostPairingConfigStore("unit-test", savedConfigPath);
        Assert.True(store.TryLoad(out var storedJson, out var error), error);

        var documentService = new PairingConfigDocumentService();
        Assert.True(documentService.TryParseAndValidateDocument(storedJson!, "com.ansight.test", out var storedDocument, out error), error);
        Assert.NotNull(storedDocument);
        Assert.Equal("cfg-saved", storedDocument!.Config.ConfigId);
        Assert.Equal(new[] { "192.168.1.10" }, storedDocument.DiscoveryHint?.HostAddresses);
    }

    [Fact]
    public async Task ConnectFromPayloadAsync_WhenPayloadHasNoHostAddressButSimulatorFallbackExists_Connects()
    {
        var savedConfigPath = CreateTempFilePath();
        var payloadDocument = CreateDocumentWithoutHostAddress(configId: "cfg-simulator");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult());
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            simulatorLocalHostAddressProvider: () => IPAddress.Loopback.ToString());

        var result = await manager.ConnectAsync(
            HostConnectionRequest.PayloadText(CreateConfigDocumentJson(payloadDocument), "pairing config"));

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-simulator", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public async Task ConnectUsingSavedConfigAsync_WhenNoSavedConfigExists_DoesNotFallbackToBundledConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledLoaderCallCount = 0;
        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ =>
                {
                    bundledLoaderCallCount++;
                    return Task.FromResult<string?>(null);
                }
            });

        var result = await manager.ConnectAsync(HostConnectionRequest.SavedConfig());

        Assert.False(result.Success);
        Assert.Contains("No saved Ansight pairing config is available.", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, bundledLoaderCallCount);
    }

    [Fact]
    public async Task AutoConnectAsync_WhenSavedConfigIsMissing_FallsBackToBundledConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection();
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled config."));
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ => Task.FromResult<string?>(CreateConfigDocumentJson(bundledDocument))
            });

        var result = await manager.ConnectAsync(HostConnectionRequest.Auto());

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public async Task AutoConnectAsync_WhenBundledConfigAssemblyContainsResource_UsesStandardLogicalName()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");

        using var hostConnection = new FakeHostConnection
        {
            ParseDocumentOverride = configJson => configJson.Trim() switch
            {
                "bundled-resource" => bundledDocument,
                _ => null
            }
        };
        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using bundled config."));
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigAssembly = typeof(HostPairingManagerTests).Assembly
            });

        var result = await manager.ConnectAsync(HostConnectionRequest.Auto());

        Assert.True(result.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public void ClearSavedConfigs_ClearsSavedStoreAndCachedHostProfile()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-saved", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        hostConnection.HasCachedProfile = true;
        using var manager = CreateManager(hostConnection, savedConfigPath);
        SaveSavedConfig(savedConfigPath, savedDocument);

        var result = manager.ClearSavedConfigs();

        Assert.True(result.Success);
        Assert.False(manager.HasSavedConfig);
        Assert.Equal(1, hostConnection.ClearCachedProfileCallCount);
        Assert.False(hostConnection.HasCachedProfile);
    }

    [Fact]
    public async Task SendClientLogAsync_ForwardsToHostConnection()
    {
        var savedConfigPath = CreateTempFilePath();
        using var hostConnection = new FakeHostConnection();
        hostConnection.SendClientLogResult = OperationResult.FromSuccess("Log sent.");
        using var manager = CreateManager(hostConnection, savedConfigPath);

        var result = await manager.SendClientLogAsync("custom app log");

        Assert.True(result.Success);
        Assert.Equal(1, hostConnection.SendClientLogCallCount);
        Assert.Equal("custom app log", hostConnection.LastClientLogLine);
    }

    [Fact]
    public async Task RefreshCapabilitiesAsync_WhenBundledConfigProbeSucceeds_UpdatesStatusSnapshot()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");
        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ => Task.FromResult<string?>(CreateConfigDocumentJson(bundledDocument))
            });

        var capabilities = await manager.RefreshCapabilitiesAsync();

        Assert.True(capabilities.CanConnectUsingBundledConfig);
        Assert.True(manager.Status.HasBundledConfig);
        Assert.Equal(HostConnectionSummaryKind.DisconnectedBundledConfigAvailable, manager.Status.SummaryKind);
    }

    [Fact]
    public async Task NotifyConfigChangedAsync_WhenBundledConfigIsAdded_UpdatesAvailability()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");
        string? bundledJson = null;
        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ => Task.FromResult<string?>(bundledJson)
            });

        var initialResult = await manager.NotifyConfigChangedAsync();
        Assert.True(initialResult.Success);
        Assert.False(manager.Status.HasBundledConfig);

        var changes = new List<HostConnectionChangedEventArgs>();
        manager.StatusChanged += (_, args) => changes.Add(args);
        bundledJson = CreateConfigDocumentJson(bundledDocument);

        var result = await manager.NotifyConfigChangedAsync();

        Assert.True(result.Success);
        Assert.Contains("now available", result.Message, StringComparison.Ordinal);
        Assert.True(manager.Status.HasBundledConfig);
        Assert.True(manager.Capabilities.CanConnectUsingBundledConfig);
        var change = Assert.Single(changes);
        Assert.True(change.Status.HasBundledConfig);
    }

    [Fact]
    public async Task NotifyConfigChangedAsync_WhenBundledConfigChanges_RaisesStatusChanged()
    {
        var savedConfigPath = CreateTempFilePath();
        var firstBundledDocument = CreateDocument(configId: "cfg-bundled-1", hostAddress: "192.168.1.20");
        var secondBundledDocument = CreateDocument(configId: "cfg-bundled-2", hostAddress: "192.168.1.21");
        var bundledJson = CreateConfigDocumentJson(firstBundledDocument);
        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ => Task.FromResult<string?>(bundledJson)
            });

        var initialResult = await manager.NotifyConfigChangedAsync();
        Assert.True(initialResult.Success);
        Assert.True(manager.Status.HasBundledConfig);

        var changes = new List<HostConnectionChangedEventArgs>();
        manager.StatusChanged += (_, args) => changes.Add(args);
        bundledJson = CreateConfigDocumentJson(secondBundledDocument);

        var result = await manager.NotifyConfigChangedAsync();

        Assert.True(result.Success);
        Assert.Contains("changed", result.Message, StringComparison.Ordinal);
        Assert.True(manager.Status.HasBundledConfig);
        Assert.True(manager.Capabilities.CanConnectUsingBundledConfig);
        var change = Assert.Single(changes);
        Assert.True(change.Status.HasBundledConfig);

        hostConnection.ConnectResults.Enqueue(CreateSuccessConnectionResult("Connected using updated bundled config."));
        var connectResult = await manager.ConnectAsync(HostConnectionRequest.BundledConfig());

        Assert.True(connectResult.Success);
        var connectedDocument = Assert.Single(hostConnection.ConnectDocuments);
        Assert.Equal("cfg-bundled-2", connectedDocument.Config.ConfigId);
    }

    [Fact]
    public async Task NotifyConfigChangedAsync_WhenBundledConfigIsRemoved_UpdatesAvailability()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "192.168.1.20");
        string? bundledJson = CreateConfigDocumentJson(bundledDocument);
        using var hostConnection = new FakeHostConnection();
        using var manager = CreateManager(
            hostConnection,
            savedConfigPath,
            new HostConnectionOptions
            {
                BundledConfigLoader = _ => Task.FromResult<string?>(bundledJson)
            });

        var initialResult = await manager.NotifyConfigChangedAsync();
        Assert.True(initialResult.Success);
        Assert.True(manager.Status.HasBundledConfig);

        var changes = new List<HostConnectionChangedEventArgs>();
        manager.StatusChanged += (_, args) => changes.Add(args);
        bundledJson = null;

        var result = await manager.NotifyConfigChangedAsync();

        Assert.True(result.Success);
        Assert.Contains("no longer available", result.Message, StringComparison.Ordinal);
        Assert.False(manager.Status.HasBundledConfig);
        Assert.False(manager.Capabilities.CanConnectUsingBundledConfig);
        var change = Assert.Single(changes);
        Assert.False(change.Status.HasBundledConfig);
    }

    [Fact]
    public void Status_WhenSavedAndCachedProfilesExist_ReportsMultipleConfigsAvailable()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-saved", hostAddress: "192.168.1.10");

        using var hostConnection = new FakeHostConnection();
        hostConnection.HasCachedProfile = true;
        SaveSavedConfig(savedConfigPath, savedDocument);
        using var manager = CreateManager(hostConnection, savedConfigPath);

        Assert.Equal(HostConnectionSummaryKind.DisconnectedMultipleConfigsAvailable, manager.Status.SummaryKind);
        Assert.True(manager.Capabilities.CanConnectUsingSavedConfig);
    }

    private static HostPairingManager CreateManager(
        FakeHostConnection hostConnection,
        string savedConfigPath,
        HostConnectionOptions? options = null,
        Func<string?>? simulatorLocalHostAddressProvider = null)
    {
        var configuredOptions = options ?? new HostConnectionOptions();
        configuredOptions.SavedConfigPath = savedConfigPath;

        return new HostPairingManager(
            hostConnection,
            configuredOptions,
            new StoredHostPairingConfigStore("unit-test", savedConfigPath),
            isRuntimeActive: () => true,
            simulatorLocalHostAddressProvider: simulatorLocalHostAddressProvider ?? (() => null));
    }

    private static ParsedPairingDocument CreateDocument(
        string configId,
        string hostAddress)
    {
        return new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite(configId: configId),
            DiscoveryHint = PairingTestDocumentFactory.CreateDiscoveryHint(hostAddress: hostAddress)
        };
    }

    private static ParsedPairingDocument CreateDocumentWithoutHostAddress(string configId)
    {
        return new ParsedPairingDocument
        {
            Config = PairingTestDocumentFactory.CreateEnrollmentInvite(configId: configId),
            DiscoveryHint = new PairingDiscoveryHint
            {
                Schema = PairingDiscoveryHint.SchemaName,
                Source = "unit-test"
            }
        };
    }

    private static void SaveSavedConfig(string savedConfigPath, ParsedPairingDocument document)
    {
        var store = new StoredHostPairingConfigStore("unit-test", savedConfigPath);
        store.Save(document);
    }

    private static string CreateConfigDocumentJson(ParsedPairingDocument document)
    {
        return PairingConfigDocumentJson.Serialize(
            new Ansight.Pairing.Models.PairingConfigDocument
            {
                Config = document.Config,
                Discovery = document.DiscoveryHint
            });
    }

    private static string CreateTempFilePath()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "Ansight.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return Path.Combine(directoryPath, "saved-config.json");
    }

    private static HostSessionActionResult CreateSuccessConnectionResult(string message = "Connected to the Ansight host.")
    {
        return HostSessionActionResult.FromSuccess(
            message,
            OpenSessionResult.FromSuccess(
                "connected",
                IPAddress.Loopback,
                new ConnectResponse
                {
                    Type = "ENROLLMENT_RESULT",
                    Ver = 2,
                    RequestId = "test-request",
                    Accepted = true,
                    Reason = "ok",
                    HostId = "host-1",
                    HostName = "Host",
                    Message = "ready",
                    WebSocketPort = 45124,
                    WebSocketPath = "/ws",
                    WebSocketToken = "token"
                }));
    }

    private static HostSessionActionResult CreateRejectedConnectionResult(string rejectionCode, string rejectionMessage)
    {
        return HostSessionActionResult.FromFailure(
            rejectionMessage,
            OpenSessionResult.FromRejected(
                IPAddress.Loopback,
                new ConnectResponse
                {
                    Type = "ENROLLMENT_RESULT",
                    Ver = 2,
                    RequestId = "test-request",
                    Accepted = false,
                    Reason = rejectionCode,
                    ReasonMessage = rejectionMessage,
                    HostId = "host-1",
                    HostName = "Host",
                    Message = rejectionMessage
                }));
    }

    private sealed class FakeHostConnection : IHostSessionConnection, IDisposable
    {
        private readonly PairingConfigDocumentService documentService = new();

        public HostConnectionState State { get; private set; } = HostConnectionState.Disconnected;

        public bool IsConnected { get; private set; }

        public bool HasCachedProfile { get; set; }

        public bool CanAttemptLocalEnrollment { get; set; }

        public string StatusSummary { get; private set; } = "No Ansight host session is connected.";

        public List<ParsedPairingDocument> ConnectDocuments { get; } = new();

        public Queue<HostSessionActionResult> ConnectResults { get; } = new();

        public Queue<HostSessionActionResult> CachedConnectResults { get; } = new();

        public Queue<HostSessionActionResult> LocalEnrollmentResults { get; } = new();

        public int CachedConnectCallCount { get; private set; }

        public int LocalEnrollmentCallCount { get; private set; }

        public int ClearCachedProfileCallCount { get; private set; }

        public int SendClientLogCallCount { get; private set; }

        public string? LastClientLogLine { get; private set; }

        public PairingConnectionOptions? LastConnectionOptions { get; private set; }

        public Func<string, ParsedPairingDocument?>? ParseDocumentOverride { get; set; }

        public OperationResult SendClientLogResult { get; set; } = OperationResult.FromFailure("WebSocket session is not open.");

        public event EventHandler<HostSessionStatusChangedEventArgs>? StatusChanged;

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

        public Task<HostSessionActionResult> ConnectAsync(
            ParsedPairingDocument document,
            string? clientName = null,
            PairingConnectionOptions? connectionOptions = null,
            IProgress<HostConnectionProgressUpdate>? progress = null,
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

        public Task<HostSessionActionResult> ConnectUsingCachedProfileAsync(
            string? clientName = null,
            IProgress<HostConnectionProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CachedConnectCallCount++;
            var result = CachedConnectResults.Count > 0
                ? CachedConnectResults.Dequeue()
                : HostSessionActionResult.FromFailure("No cached profile.");
            ApplyState(result);
            return Task.FromResult(result);
        }

        public Task<HostSessionActionResult> ConnectUsingLocalEnrollmentAsync(
            string? clientName = null,
            IProgress<HostConnectionProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LocalEnrollmentCallCount++;
            var result = LocalEnrollmentResults.Count > 0
                ? LocalEnrollmentResults.Dequeue()
                : HostSessionActionResult.FromFailure("Local enrollment failed.");
            ApplyState(result);
            return Task.FromResult(result);
        }

        public Task<OperationResult> SendClientLogAsync(
            string logLine,
            IProgress<HostConnectionProgressUpdate>? progress = null,
            CancellationToken cancellationToken = default)
        {
            SendClientLogCallCount++;
            LastClientLogLine = logLine;
            return Task.FromResult(SendClientLogResult);
        }

        public Task<HostSessionActionResult> DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            State = HostConnectionState.Disconnected;
            StatusSummary = "No Ansight host session is connected.";
            RaiseStatusChanged();
            return Task.FromResult(HostSessionActionResult.FromSuccess("Disconnected."));
        }

        public HostSessionActionResult ClearCachedProfile()
        {
            ClearCachedProfileCallCount++;
            HasCachedProfile = false;
            RaiseStatusChanged();
            return HostSessionActionResult.FromSuccess("Cleared the cached Ansight host session.");
        }

        public void Dispose()
        {
        }

        private void ApplyState(HostSessionActionResult result)
        {
            IsConnected = result.Success;
            State = result.Success ? HostConnectionState.Connected : HostConnectionState.Disconnected;
            StatusSummary = result.Message;
            RaiseStatusChanged();
        }

        private void RaiseStatusChanged()
        {
            StatusChanged?.Invoke(this, new HostSessionStatusChangedEventArgs(
                State,
                IsConnected,
                HasCachedProfile,
                StatusSummary));
        }
    }
}
