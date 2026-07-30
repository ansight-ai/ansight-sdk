using System.Net;
using Ansight.Input;
using Ansight.Pairing;

namespace Ansight.IntegrationTests;

public sealed class HostPairingManagerIntegrationTests
{
    [Fact]
    public async Task AutoConnectAsync_WhenBundledConfigExists_ConnectsThroughHostConnectionManager()
    {
        var savedConfigPath = CreateTempFilePath();
        var bundledDocument = CreateDocument(configId: "cfg-bundled", hostAddress: "127.0.0.1");

        var runtime = CreateRuntime();
        runtime.Activate();
        using var sessionClient = new FakeHostConnectionSessionClient();
        sessionClient.OpenSessionResult = CreateOpenSuccess();
        using var hostConnection = new HostSessionManager(runtime, HostAutoProbeOptions.DisabledDefault, sessionClient);
        using var hostPairing = new HostPairingManager(
            hostConnection,
            new HostConnectionOptions
            {
                SavedConfigPath = savedConfigPath,
                BundledConfigLoader = _ => Task.FromResult<string?>(CreateConfigDocumentJson(bundledDocument))
            },
            new StoredHostPairingConfigStore("integration-test", savedConfigPath));

        var result = await hostPairing.ConnectAsync(HostConnectionRequest.Auto());

        Assert.True(result.Success, result.Message);
        Assert.True(hostConnection.IsConnected);
        Assert.Equal(1, sessionClient.OpenSessionCallCount);
        Assert.Equal(1, sessionClient.StartMetricsStreamingCallCount);
        Assert.Equal("cfg-bundled", sessionClient.LastOpenedDocument?.Config.ConfigId);

        runtime.Deactivate();
    }

    [Fact]
    public async Task ConnectFromPayloadAsync_WhenPairingConfigIsProvided_ConnectsThatConfig()
    {
        var savedConfigPath = CreateTempFilePath();
        var savedDocument = CreateDocument(configId: "cfg-base", hostAddress: "127.0.0.1");

        var runtime = CreateRuntime();
        runtime.Activate();
        using var sessionClient = new FakeHostConnectionSessionClient();
        sessionClient.OpenSessionResult = CreateOpenSuccess();
        using var hostConnection = new HostSessionManager(runtime, HostAutoProbeOptions.DisabledDefault, sessionClient);
        using var hostPairing = new HostPairingManager(
            hostConnection,
            new HostConnectionOptions
            {
                SavedConfigPath = savedConfigPath
            },
            new StoredHostPairingConfigStore("integration-test", savedConfigPath));
        SaveSavedConfig(savedConfigPath, savedDocument);

        var payload = PairingConfigDocumentJson.Serialize(
            new Ansight.Pairing.Models.PairingConfigDocument
            {
                Config = CreateEnrollmentInvite(configId: "cfg-override"),
                Discovery = CreateDiscoveryHint(hostAddress: "127.0.0.1")
            });

        var result = await hostPairing.ConnectAsync(HostConnectionRequest.PayloadText(payload, "pairing config"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, sessionClient.OpenSessionCallCount);
        Assert.Equal(1, sessionClient.StartMetricsStreamingCallCount);
        Assert.Equal("cfg-override", sessionClient.LastOpenedDocument?.Config.ConfigId);
        Assert.Contains("Streaming live metrics to Host", hostConnection.StatusSummary, StringComparison.Ordinal);

        runtime.Deactivate();
    }

    private static RuntimeImpl CreateRuntime()
    {
        return new RuntimeImpl(
            Options.CreateBuilder()
                .WithoutHostAutoProbe()
                .Build());
    }

    private static ParsedPairingDocument CreateDocument(
        string configId,
        string hostAddress)
    {
        return new ParsedPairingDocument
        {
            Config = CreateEnrollmentInvite(configId: configId),
            DiscoveryHint = CreateDiscoveryHint(hostAddress: hostAddress)
        };
    }

    private static PairingConfig CreateEnrollmentInvite(
        string configId,
        string appId = "com.ansight.test")
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        return new PairingConfig
        {
            Schema = PairingConfig.SchemaName,
            ConfigId = configId,
            AppId = appId,
            AppName = "Ansight Integration Test",
            IssuedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt,
            MinProtocolVersion = 2,
            AllowedTransports = ["ws"],
            Host = new PairingHost
            {
                HostId = "host-1",
                HostName = "test-host",
                DiscoveryPort = 41000
            },
            Enrollment = new PairingEnrollment
            {
                Secret = PairingCrypto.CreateBase64UrlRandom(32),
                ExpiresAt = expiresAt,
                GrantExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
                MaxUses = 1,
                MaxScopes = ["Read"],
                AllowCritical = false
            }
        };
    }

    private static PairingDiscoveryHint CreateDiscoveryHint(string hostAddress)
    {
        return new PairingDiscoveryHint
        {
            Schema = PairingDiscoveryHint.SchemaName,
            Source = "integration-test",
            HostAddresses = new[] { hostAddress },
            HostName = "test-host",
            CapturedAt = DateTimeOffset.UtcNow
        };
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

    private static void SaveSavedConfig(string savedConfigPath, ParsedPairingDocument document)
    {
        var store = new StoredHostPairingConfigStore("integration-test", savedConfigPath);
        store.Save(document);
    }

    private static string CreateTempFilePath()
    {
        var directoryPath = Path.Combine(Path.GetTempPath(), "Ansight.IntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return Path.Combine(directoryPath, "saved-config.json");
    }

    private static OpenSessionResult CreateOpenSuccess()
    {
        return OpenSessionResult.FromSuccess(
            "connected",
            IPAddress.Loopback,
            new ConnectResponse
            {
                Type = "ENROLLMENT_RESULT",
                Ver = 2,
                RequestId = "integration-request",
                Accepted = true,
                Reason = "ok",
                HostId = "host-1",
                HostName = "Host",
                Message = "ready",
                WebSocketPort = 45124,
                WebSocketPath = "/ws",
                WebSocketToken = "token"
            });
    }

    private sealed class FakeHostConnectionSessionClient : IHostConnectionSessionClient
    {
        private readonly PairingConfigDocumentService documentService = new();

        public event EventHandler? SessionClosed;

        public bool IsSessionOpen { get; private set; }

        public bool HasCachedPairingProfile { get; set; }

        public int OpenSessionCallCount { get; private set; }

        public int OpenCachedSessionCallCount { get; private set; }

        public int StartMetricsStreamingCallCount { get; private set; }

        public int StartTouchCaptureStreamingCallCount { get; private set; }

        public OpenSessionResult OpenSessionResult { get; set; } = OpenSessionResult.FromFailure("no session result queued");

        public OpenSessionResult OpenCachedSessionResult { get; set; } = OpenSessionResult.FromFailure("no cached session result queued");

        public ParsedPairingDocument? LastOpenedDocument { get; private set; }

        public bool TryParseAndValidateDocument(string configJson, out ParsedPairingDocument? document, out string error)
        {
            return documentService.TryParseAndValidateDocument(configJson, "com.ansight.test", out document, out error);
        }

        public Task<OpenSessionResult> OpenSessionAsync(
            ParsedPairingDocument document,
            string clientName,
            PairingConnectionOptions? options,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            OpenSessionCallCount++;
            LastOpenedDocument = document;
            IsSessionOpen = OpenSessionResult.Success;
            return Task.FromResult(OpenSessionResult);
        }

        public Task<OpenSessionResult> OpenCachedSessionAsync(
            string? clientName,
            PairingConnectionOptions? options,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            OpenCachedSessionCallCount++;
            IsSessionOpen = OpenCachedSessionResult.Success;
            return Task.FromResult(OpenCachedSessionResult);
        }

        public Task<OperationResult> StartMetricsStreamingAsync(
            IDataSink dataSink,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            StartMetricsStreamingCallCount++;
            return Task.FromResult(OperationResult.FromSuccess("streaming"));
        }

        public Task<OperationResult> StartTouchCaptureStreamingAsync(
            TouchCaptureHub touchCaptureHub,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            StartTouchCaptureStreamingCallCount++;
            return Task.FromResult(OperationResult.FromSuccess("touch capture streaming"));
        }

        public Task<OperationResult> SendClientLogAsync(
            string logLine,
            IProgress<HostConnectionProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(OperationResult.FromSuccess("log sent"));
        }

        public Task<OperationResult> CloseSessionAsync(CancellationToken cancellationToken)
        {
            var wasOpen = IsSessionOpen;
            IsSessionOpen = false;
            if (wasOpen)
            {
                SessionClosed?.Invoke(this, EventArgs.Empty);
            }

            return Task.FromResult(OperationResult.FromSuccess("closed"));
        }

        public void ClearCachedPairingProfile()
        {
            HasCachedPairingProfile = false;
        }

        public string ResolveClientName(string? overrideClientName)
        {
            return string.IsNullOrWhiteSpace(overrideClientName)
                ? "Unit Test App"
                : overrideClientName.Trim();
        }

        public void Dispose()
        {
        }
    }
}
