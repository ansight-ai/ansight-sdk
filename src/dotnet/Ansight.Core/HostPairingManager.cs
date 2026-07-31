using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class HostPairingManager : IHostConnection, IDisposable
{
    private static readonly HashSet<string> StoredProfileResetReasonCodes = new(StringComparer.Ordinal)
    {
        PairingFailureCodes.EnrollmentRequired,
        PairingFailureCodes.EnrollmentExpired,
        PairingFailureCodes.EnrollmentConsumed,
        PairingFailureCodes.AccessTokenInvalid,
        PairingFailureCodes.RegistrationExpired
    };

    private readonly IHostSessionConnection hostConnection;
    private readonly HostConnectionOptions options;
    private readonly StoredHostPairingConfigStore savedConfigStore;
    private readonly PairingConfigDocumentService pairingDocumentService = new();
    private readonly Func<bool> isRuntimeActive;
    private readonly Func<string?> simulatorLocalHostAddressProvider;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Lock statusGate = new();
    private HostConnectionStatus status;
    private HostConnectionCapabilities capabilities;
    private BundledConfigSnapshot bundledConfigSnapshot = BundledConfigSnapshot.None;
    private bool hasBundledConfig;
    private bool disposed;

    internal HostPairingManager(
        IHostSessionConnection hostConnection,
        HostConnectionOptions options,
        StoredHostPairingConfigStore? savedConfigStore = null,
        Func<bool>? isRuntimeActive = null,
        Func<string?>? simulatorLocalHostAddressProvider = null)
    {
        this.hostConnection = hostConnection ?? throw new ArgumentNullException(nameof(hostConnection));
        this.options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        this.savedConfigStore = savedConfigStore
                                ?? new StoredHostPairingConfigStore(
                                         StoredPairingDocumentCache.ResolveCacheKey(AutomaticDeviceAppProfileProvider.Instance),
                                         this.options.SavedConfigPath);
        this.isRuntimeActive = isRuntimeActive ?? (() => Runtime.IsActive);
        this.simulatorLocalHostAddressProvider = simulatorLocalHostAddressProvider ?? PairingSimulatorLocalHostAddress.Resolve;
        hasBundledConfig = false;
        status = BuildStatusSnapshot(hasBundledConfig);
        capabilities = BuildCapabilities(hasBundledConfig);
        this.hostConnection.StatusChanged += HandleHostConnectionStatusChanged;
        UpdateStatusAndCapabilities(hasBundledConfig);
    }

    public bool HasSavedConfig => savedConfigStore.HasStoredConfig;

    public bool IsConnected => hostConnection.IsConnected;

    public HostConnectionStatus Status
    {
        get
        {
            lock (statusGate)
            {
                return status;
            }
        }
    }

    public HostConnectionCapabilities Capabilities
    {
        get
        {
            lock (statusGate)
            {
                return capabilities;
            }
        }
    }

    public event EventHandler<HostConnectionChangedEventArgs>? StatusChanged;

    public async Task<HostConnectionCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var resolvedBundledConfig = await ResolveBundledConfigSnapshotAsync(cancellationToken);
        bundledConfigSnapshot = resolvedBundledConfig;
        return UpdateStatusAndCapabilities(resolvedBundledConfig.HasConfig);
    }

    public async Task<HostConnectionResult> NotifyConfigChangedAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var previousSnapshot = bundledConfigSnapshot;
            var nextSnapshot = await ResolveBundledConfigSnapshotAsync(cancellationToken);
            var configChanged = HasBundledConfigChanged(previousSnapshot, nextSnapshot);

            bundledConfigSnapshot = nextSnapshot;
            UpdateStatusAndCapabilities(nextSnapshot.HasConfig, forceChangedEvent: configChanged);

            var resultSource = ResolveConfigChangeSource(previousSnapshot, nextSnapshot);
            if (nextSnapshot.IsFailure)
            {
                return HostConnectionResult.FromFailure(
                    $"Failed to refresh the Ansight host config: {nextSnapshot.Error}",
                    HostConnectionActionKind.NotifyConfigChanged,
                    resultSource);
            }

            return HostConnectionResult.FromSuccess(
                DescribeConfigChange(previousSnapshot, nextSnapshot, configChanged),
                HostConnectionActionKind.NotifyConfigChanged,
                resultSource);
        }
        finally
        {
            operationGate.Release();
        }
    }

    private bool CanReadRequest(HostConnectionRequestKind kind)
    {
        if (options.ConfigReader is null)
        {
            return false;
        }

        try
        {
            return options.ConfigReader.CanRead(kind);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to resolve config reader support for {kind}: {ex.Message}");
            return false;
        }
    }

    public bool TryParseConfigDocument(string payload, out PairingConfigDocument? config, out string error)
    {
        if (!pairingDocumentService.TryParseConfigDocument(payload, out var parsedConfig, out error) || parsedConfig is null)
        {
            config = null;
            return false;
        }

        var resolvedConfig = ResolveConfigDocument(parsedConfig, HostConnectionSource.Payload, "Using supplied pairing config.");
        if (!resolvedConfig.Success || resolvedConfig.Document is null)
        {
            config = null;
            error = resolvedConfig.Message;
            return false;
        }

        config = PairingConfigDocumentService.CreateConfigDocument(resolvedConfig.Document);
        error = string.Empty;
        return true;
    }

    public async Task<HostConnectionResult> ConnectAsync(
        HostConnectionRequest? request = null,
        string? clientName = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        request ??= HostConnectionRequest.Auto();

        return request.Kind switch
        {
            HostConnectionRequestKind.Auto => await ConnectAutoAsync(clientName, progress, cancellationToken),
            HostConnectionRequestKind.SavedConfig => await ConnectUsingSavedConfigAsync(clientName, progress, cancellationToken),
            HostConnectionRequestKind.BundledConfig => await ConnectUsingBundledConfigAsync(clientName, progress, cancellationToken),
            HostConnectionRequestKind.Config => await ConnectConfigAsync(request.Config, clientName, progress, cancellationToken),
            HostConnectionRequestKind.Payload => await ConnectFromPayloadAsync(
                request.Payload,
                request.SourceDescription,
                clientName,
                progress,
                cancellationToken),
            HostConnectionRequestKind.File or HostConnectionRequestKind.QrCode => await ConnectFromReaderAsync(
                request,
                clientName,
                progress,
                cancellationToken),
            _ => HostConnectionResult.FromFailure(
                $"Unsupported host connection request kind '{request.Kind}'.",
                HostConnectionActionKind.Connect)
        };
    }

    public Task<OperationResult> SendClientLogAsync(
        string logLine,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return hostConnection.SendClientLogAsync(logLine, progress, cancellationToken);
    }

    private async Task<HostConnectionResult> ConnectAutoAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostConnectionResult.FromSuccess(
                    hostConnection.StatusSummary,
                    HostConnectionActionKind.AutoConnect,
                    HostConnectionSource.HostConnection);
            }

            HostConnectionResult? localEnrollmentResult = null;
            if (hostConnection.CanAttemptLocalEnrollment)
            {
                var localResult = await hostConnection.ConnectUsingLocalEnrollmentAsync(
                    clientName,
                    progress,
                    cancellationToken);
                localEnrollmentResult = ToPairingResult(
                    localResult,
                    HostConnectionActionKind.AutoConnect);
                if (localEnrollmentResult.Success)
                {
                    return localEnrollmentResult;
                }
            }

            if (hostConnection.HasCachedProfile)
            {
                var cachedProfileResult = await hostConnection.ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);
                if (cachedProfileResult.Success)
                {
                    return ToPairingResult(cachedProfileResult, HostConnectionActionKind.AutoConnect);
                }
            }

            if (HasSavedConfig)
            {
                var savedConfigResult = await ConnectUsingSavedConfigCoreAsync(
                    clientName,
                    progress,
                    cancellationToken,
                    HostConnectionActionKind.AutoConnect);
                if (savedConfigResult.Success ||
                    (localEnrollmentResult is null &&
                     !ShouldRetryWithBundledConfig(savedConfigResult.Source, savedConfigResult.ReasonCode)))
                {
                    return savedConfigResult;
                }
            }

            var bundledResult = await ConnectUsingStandardBundledConfigCoreAsync(
                clientName,
                progress,
                cancellationToken,
                HostConnectionActionKind.AutoConnect);
            if (bundledResult.Success)
            {
                return bundledResult;
            }

            return localEnrollmentResult ?? bundledResult;
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledConfig);
            operationGate.Release();
        }
    }

    private async Task<HostConnectionResult> ConnectConfigAsync(
        PairingConfigDocument? config,
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);

        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var resolvedDocument = ResolveConfigDocument(config, HostConnectionSource.Payload, "Using supplied pairing config.");
            if (!resolvedDocument.Success || resolvedDocument.Document is null)
            {
                return HostConnectionResult.FromFailure(
                    resolvedDocument.Message,
                    HostConnectionActionKind.Connect,
                    resolvedDocument.Source);
            }

            var connectResult = await ConnectResolvedDocumentAsync(
                resolvedDocument,
                clientName,
                progress,
                cancellationToken,
                HostConnectionActionKind.Connect);
            return ToPairingResult(connectResult, HostConnectionActionKind.Connect);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledConfig);
            operationGate.Release();
        }
    }

    private async Task<HostConnectionResult> ConnectUsingSavedConfigAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostConnectionResult.FromSuccess(
                    hostConnection.StatusSummary,
                    HostConnectionActionKind.ConnectUsingSavedConfig,
                    HostConnectionSource.HostConnection);
            }

            if (!HasSavedConfig)
            {
                return HostConnectionResult.FromFailure(
                    "No saved Ansight pairing config is available.",
                    HostConnectionActionKind.ConnectUsingSavedConfig,
                    HostConnectionSource.SavedConfig);
            }

            return await ConnectUsingSavedConfigCoreAsync(
                clientName,
                progress,
                cancellationToken,
                HostConnectionActionKind.ConnectUsingSavedConfig);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledConfig);
            operationGate.Release();
        }
    }

    private async Task<HostConnectionResult> ConnectUsingBundledConfigAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectUsingBundledConfigCoreAsync(
                clientName,
                progress,
                cancellationToken,
                HostConnectionActionKind.ConnectUsingBundledConfig);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledConfig);
            operationGate.Release();
        }
    }

    private async Task<HostConnectionResult> ConnectFromPayloadAsync(
        string? payload,
        string? sourceDescription,
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return HostConnectionResult.FromFailure(
                "Paste or load a pairing config.",
                HostConnectionActionKind.ConnectFromPayload,
                HostConnectionSource.Payload);
        }

        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectFromPayloadCoreAsync(
                payload,
                sourceDescription,
                clientName,
                progress,
                cancellationToken);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledConfig);
            operationGate.Release();
        }
    }

    private async Task<HostConnectionResult> ConnectFromReaderAsync(
        HostConnectionRequest request,
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (options.ConfigReader is null || !CanReadRequest(request.Kind))
        {
            return HostConnectionResult.FromFailure(
                $"No host config reader is registered for {request.Kind}.",
                HostConnectionActionKind.ConnectFromPayload,
                HostConnectionSource.ConfigReader);
        }

        string? payload;
        try
        {
            payload = await options.ConfigReader.ReadConfigPayloadAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HostConnectionResult.FromFailure(
                $"Failed to read a pairing config: {ex.Message}",
                HostConnectionActionKind.ConnectFromPayload,
                HostConnectionSource.ConfigReader);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return HostConnectionResult.FromFailure(
                "No pairing config was provided.",
                HostConnectionActionKind.ConnectFromPayload,
                HostConnectionSource.ConfigReader);
        }

        return await ConnectFromPayloadAsync(
            payload,
            request.SourceDescription,
            clientName,
            progress,
            cancellationToken);
    }

    public async Task<HostConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await hostConnection.DisconnectAsync(cancellationToken);
            return ToPairingResult(result, HostConnectionActionKind.Disconnect);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledConfig);
            operationGate.Release();
        }
    }

    public HostConnectionResult ClearSavedConfigs()
    {
        operationGate.Wait();
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostConnectionResult.FromFailure(
                    "Disconnect from Ansight host before clearing saved configs.",
                    HostConnectionActionKind.ClearSavedConfigs,
                    HostConnectionSource.SavedConfig);
            }

            savedConfigStore.Clear();
            var cachedProfileResult = hostConnection.ClearCachedProfile();
            if (!cachedProfileResult.Success)
            {
                return HostConnectionResult.FromFailure(
                    cachedProfileResult.Message,
                    HostConnectionActionKind.ClearSavedConfigs,
                    cachedProfileResult.Source,
                    cachedProfileResult.ReasonCode);
            }

            return HostConnectionResult.FromSuccess(
                "Cleared saved Ansight host configs.",
                HostConnectionActionKind.ClearSavedConfigs,
                HostConnectionSource.SavedConfig);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            return HostConnectionResult.FromFailure(
                $"Failed to clear saved Ansight host configs: {ex.Message}",
                HostConnectionActionKind.ClearSavedConfigs,
                HostConnectionSource.SavedConfig);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledConfig);
            operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        hostConnection.StatusChanged -= HandleHostConnectionStatusChanged;
        operationGate.Dispose();
    }

    private async Task<HostConnectionResult> ConnectFromPayloadCoreAsync(
        string payload,
        string? sourceDescription,
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var resolvedDocument = await ResolvePairingDocumentAsync(
            payload,
            sourceDescription,
            cancellationToken);
        if (!resolvedDocument.Success || resolvedDocument.Document is null)
        {
            return HostConnectionResult.FromFailure(
                resolvedDocument.Message,
                HostConnectionActionKind.ConnectFromPayload,
                resolvedDocument.Source);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            resolvedDocument,
            clientName,
            progress,
            cancellationToken,
            HostConnectionActionKind.ConnectFromPayload);

        return ToPairingResult(connectResult, HostConnectionActionKind.ConnectFromPayload);
    }

    private async Task<HostConnectionResult> ConnectUsingSavedConfigCoreAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken,
        HostConnectionActionKind actionKind)
    {
        var savedConfig = await TryResolveSavedPairingDocumentAsync();
        if (!savedConfig.Success || savedConfig.Document is null)
        {
            return HostConnectionResult.FromFailure(
                savedConfig.Message,
                actionKind,
                savedConfig.Source);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            savedConfig,
            clientName,
            progress,
            cancellationToken,
            actionKind);
        return ToPairingResult(connectResult, actionKind);
    }

    private async Task<HostConnectionResult> ConnectUsingBundledConfigCoreAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken,
        HostConnectionActionKind actionKind)
    {
        var bundledDocument = await TryResolveBundledPairingDocumentAsync(cancellationToken);
        if (!bundledDocument.Success || bundledDocument.Document is null)
        {
            return HostConnectionResult.FromFailure(
                bundledDocument.Message,
                actionKind,
                bundledDocument.Source);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            bundledDocument,
            clientName,
            progress,
            cancellationToken,
            actionKind);
        return ToPairingResult(connectResult, actionKind);
    }

    private async Task<HostConnectionResult> ConnectUsingStandardBundledConfigCoreAsync(
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken,
        HostConnectionActionKind actionKind)
    {
        var bundledDocument = await TryResolveBundledConfigDocumentAsync(cancellationToken);
        if (!bundledDocument.Success || bundledDocument.Document is null)
        {
            return HostConnectionResult.FromFailure(
                bundledDocument.Message,
                actionKind,
                bundledDocument.Source);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            bundledDocument,
            clientName,
            progress,
            cancellationToken,
            actionKind);
        return ToPairingResult(connectResult, actionKind);
    }

    private Task<ResolvedPairingDocument> ResolvePairingDocumentAsync(
        string payload,
        string? sourceDescription,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (PairingConfigCodeGenerator.TryParse(payload, out var compactConfigDocument) && compactConfigDocument is not null)
        {
            return Task.FromResult(ResolveConfigDocument(
                compactConfigDocument,
                HostConnectionSource.Payload,
                $"Loaded {sourceDescription ?? "pairing config code"}."));
        }

        if (!pairingDocumentService.TryParseConfigDocument(payload, out var configDocument, out var error) || configDocument is null)
        {
            return Task.FromResult(ResolvedPairingDocument.FromFailure(
                string.IsNullOrWhiteSpace(error)
                    ? "Pairing payloads must be pairing configs or compact pairing config codes."
                    : error));
        }

        return Task.FromResult(ResolveConfigDocument(
            configDocument,
            HostConnectionSource.Payload,
            $"Loaded {sourceDescription ?? "pairing config"}."));
    }

    private Task<ResolvedPairingDocument> TryResolveSavedPairingDocumentAsync()
    {
        if (!savedConfigStore.TryLoad(out var json, out var error) || string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(ResolvedPairingDocument.FromFailure(error));
        }

        if (!hostConnection.TryParseAndValidateDocument(json, out var document, out error) || document is null)
        {
            savedConfigStore.Clear();
            var clearedError = string.IsNullOrWhiteSpace(error)
                ? "Saved pairing config is invalid and was cleared."
                : $"{error} Saved pairing config was cleared.";
            return Task.FromResult(ResolvedPairingDocument.FromFailure(clearedError));
        }

        return Task.FromResult(ResolvedPairingDocument.FromSuccess(
            document,
            "Using saved pairing config.",
            HostConnectionSource.SavedConfig));
    }

    private async Task<ResolvedPairingDocument> TryResolveBundledPairingDocumentAsync(CancellationToken cancellationToken)
    {
        var bundledDocument = await TryResolveBundledConfigDocumentAsync(cancellationToken);
        if (bundledDocument.Success)
        {
            return bundledDocument;
        }

        return ResolvedPairingDocument.FromFailure("No bundled pairing config is available.");
    }

    private Task<ResolvedPairingDocument> TryResolveBundledConfigDocumentAsync(CancellationToken cancellationToken)
    {
        return TryLoadBundledDocumentAsync(
            ResolveBundledDocumentLoader(HostConnectionSource.BundledConfig),
            "Using bundled pairing config.",
            HostConnectionSource.BundledConfig,
            cancellationToken);
    }

    private async Task<ResolvedPairingDocument> TryLoadBundledDocumentAsync(
        Func<CancellationToken, Task<string?>>? loader,
        string successMessage,
        HostConnectionSource source,
        CancellationToken cancellationToken)
    {
        if (loader is null)
        {
            RecordBundledConfigSnapshot(BundledConfigSnapshot.Unavailable(source));
            return ResolvedPairingDocument.FromFailure("No bundled pairing config is available.");
        }

        string? json;
        try
        {
            json = await loader(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            RecordBundledConfigSnapshot(BundledConfigSnapshot.FromFailure(source, ex.Message));
            return ResolvedPairingDocument.FromFailure($"Failed to load a bundled pairing config: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            RecordBundledConfigSnapshot(BundledConfigSnapshot.Unavailable(source));
            return ResolvedPairingDocument.FromFailure("No bundled pairing config is available.");
        }

        if (!hostConnection.TryParseAndValidateDocument(json, out var document, out var error) || document is null)
        {
            RecordBundledConfigSnapshot(BundledConfigSnapshot.FromFailure(source, error));
            Logger.Warning($"Ignoring invalid bundled pairing config. {error}");
            return ResolvedPairingDocument.FromFailure(error);
        }

        RecordBundledConfigSnapshot(BundledConfigSnapshot.FromDocument(document, source));
        return ResolvedPairingDocument.FromSuccess(
            document,
            successMessage,
            source);
    }

    private async Task<HostSessionActionResult> ConnectResolvedDocumentAsync(
        ResolvedPairingDocument resolvedDocument,
        string? clientName,
        IProgress<HostConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken,
        HostConnectionActionKind actionKind)
    {
        ArgumentNullException.ThrowIfNull(resolvedDocument.Document);

        var discoveryPort = PairingDiscoveryPortResolver.Resolve(resolvedDocument.Document, options.DiscoveryPort);
        LogPairingExpectation(resolvedDocument.Document, discoveryPort);

        if (PairingDiscoveryHintHostAddresses.ResolveCandidates(
                resolvedDocument.Document.DiscoveryHint,
                hostAddressOverride: null,
                ResolveSimulatorLocalHostAddress()).Length == 0)
        {
            return HostSessionActionResult.FromFailure(
                "A current Ansight host address is required. Import a fresh pairing config or compact pairing config code.",
                kind: actionKind,
                source: resolvedDocument.Source,
                reasonCode: PairingFailureCodes.HostAddressRequired);
        }

        var connectResult = await hostConnection.ConnectAsync(
            resolvedDocument.Document,
            clientName,
            new PairingConnectionOptions
            {
                DiscoveryPort = discoveryPort,
                AllowCellularConnections = options.AllowCellularConnections
            },
            progress,
            cancellationToken);
        connectResult = connectResult with
        {
            Kind = actionKind,
            Source = resolvedDocument.Source,
            ReasonCode = connectResult.ReasonCode ?? connectResult.SessionResult?.RejectionCode
        };
        if (connectResult.Success)
        {
            try
            {
                savedConfigStore.Save(resolvedDocument.Document);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to save the Ansight pairing config: {ex.Message}");
            }
        }
        else if (resolvedDocument.Source == HostConnectionSource.SavedConfig)
        {
            var rejectionCode = connectResult.ReasonCode;
            if (!string.IsNullOrWhiteSpace(rejectionCode) &&
                StoredProfileResetReasonCodes.Contains(rejectionCode))
            {
                savedConfigStore.Clear();
            }
        }

        return connectResult;
    }

    private string? ResolveSimulatorLocalHostAddress()
    {
        try
        {
            var address = simulatorLocalHostAddressProvider();
            return string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        }
        catch (Exception ex)
        {
            Logger.Info($"Simulator host-address detection failed: {ex.Message}");
            return null;
        }
    }

    private static HostConnectionResult ToPairingResult(
        HostSessionActionResult connectResult,
        HostConnectionActionKind fallbackKind)
    {
        var actionKind = connectResult.Kind == HostConnectionActionKind.None ? fallbackKind : connectResult.Kind;
        return connectResult.Success
            ? HostConnectionResult.FromSuccess(connectResult.Message, actionKind, connectResult.Source, connectResult.ReasonCode)
            : HostConnectionResult.FromFailure(connectResult.Message, actionKind, connectResult.Source, connectResult.ReasonCode);
    }

    private static bool ShouldRetryWithBundledConfig(
        HostConnectionSource source,
        string? reasonCode)
    {
        return source == HostConnectionSource.SavedConfig &&
               !string.IsNullOrWhiteSpace(reasonCode) &&
               (StoredProfileResetReasonCodes.Contains(reasonCode) ||
                string.Equals(reasonCode, PairingFailureCodes.HostAddressRequired, StringComparison.Ordinal));
    }

    private async Task<BundledConfigSnapshot> ResolveBundledConfigSnapshotAsync(CancellationToken cancellationToken)
    {
        var bundledConfig = await TryResolveBundledConfigSnapshotAsync(
            ResolveBundledDocumentLoader(HostConnectionSource.BundledConfig),
            HostConnectionSource.BundledConfig,
            cancellationToken);
        if (bundledConfig.HasConfig)
        {
            return bundledConfig;
        }

        return bundledConfig;
    }

    private async Task<BundledConfigSnapshot> TryResolveBundledConfigSnapshotAsync(
        Func<CancellationToken, Task<string?>>? loader,
        HostConnectionSource source,
        CancellationToken cancellationToken)
    {
        if (loader is null)
        {
            return BundledConfigSnapshot.None;
        }

        try
        {
            var json = await loader(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return BundledConfigSnapshot.Unavailable(source);
            }

            if (!hostConnection.TryParseAndValidateDocument(json, out var document, out var error) || document is null)
            {
                return BundledConfigSnapshot.FromFailure(source, error);
            }

            return BundledConfigSnapshot.FromDocument(document, source);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to probe {DescribeSource(source)} availability: {ex.Message}");
            return BundledConfigSnapshot.FromFailure(source, ex.Message);
        }
    }

    private Func<CancellationToken, Task<string?>>? ResolveBundledDocumentLoader(HostConnectionSource source)
    {
        return source switch
        {
            HostConnectionSource.BundledConfig => ResolveBundledDocumentLoader(
                options.BundledConfigLoader,
                HostConnectionOptions.BundledConfigAssetName),
            _ => null
        };
    }

    private Func<CancellationToken, Task<string?>>? ResolveBundledDocumentLoader(
        Func<CancellationToken, Task<string?>>? explicitLoader,
        string logicalName,
        bool useDefaultAssemblyFallback = false)
    {
        if (explicitLoader is not null)
        {
            return explicitLoader;
        }

        var bundledConfigAssembly = options.BundledConfigAssembly;
        if (bundledConfigAssembly is null)
        {
            return useDefaultAssemblyFallback
                ? cancellationToken => LoadDefaultEmbeddedResourceTextAsync(logicalName, cancellationToken)
                : null;
        }

        return cancellationToken => LoadEmbeddedResourceTextAsync(bundledConfigAssembly, logicalName, cancellationToken);
    }

    private static async Task<string?> LoadDefaultEmbeddedResourceTextAsync(
        string logicalName,
        CancellationToken cancellationToken)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            var text = await LoadEmbeddedResourceTextAsync(entryAssembly, logicalName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assembly.IsDynamic || ReferenceEquals(assembly, entryAssembly))
            {
                continue;
            }

            string[] resourceNames;
            try
            {
                resourceNames = assembly.GetManifestResourceNames();
            }
            catch
            {
                continue;
            }

            if (!resourceNames.Contains(logicalName, StringComparer.Ordinal))
            {
                continue;
            }

            var text = await LoadEmbeddedResourceTextAsync(assembly, logicalName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static async Task<string?> LoadEmbeddedResourceTextAsync(
        Assembly assembly,
        string logicalName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var stream = assembly.GetManifestResourceStream(logicalName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return text;
    }

    private static void LogPairingExpectation(ParsedPairingDocument document, int discoveryPort)
    {
        var expectedHostAddress = PairingDiscoveryHintHostAddresses.ResolvePrimary(document.DiscoveryHint);
        var expectedWifiName = FirstNonEmpty(document.DiscoveryHint?.WifiName);
        var expectedHostName = FirstNonEmpty(document.DiscoveryHint?.HostName, document.Config.Host.HostName);

        Logger.Info(
            $"Ansight pairing expectation: wifi={expectedWifiName ?? "Unknown"} " +
            $"host={expectedHostName ?? "Unknown"} " +
            $"hostAddress={expectedHostAddress ?? "Unknown"} " +
            $"discoveryPort={discoveryPort}");
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string DescribeSource(HostConnectionSource source)
    {
        return source switch
        {
            HostConnectionSource.BundledConfig => "the bundled pairing config",
            _ => "the bundled config source"
        };
    }

    private ResolvedPairingDocument ResolveConfigDocument(
        PairingConfigDocument configDocument,
        HostConnectionSource source,
        string successMessage)
    {
        if (!hostConnection.TryParseAndValidateDocument(
                PairingConfigDocumentJson.Serialize(configDocument, indented: false),
                out var document,
                out var error) || document is null)
        {
            return ResolvedPairingDocument.FromFailure(error);
        }

        return ResolvedPairingDocument.FromSuccess(document, successMessage, source);
    }

    private void HandleHostConnectionStatusChanged(object? sender, HostSessionStatusChangedEventArgs e)
    {
        UpdateStatusAndCapabilities(hasBundledConfig);
    }

    private void RecordBundledConfigSnapshot(BundledConfigSnapshot snapshot)
    {
        bundledConfigSnapshot = snapshot;
        hasBundledConfig = snapshot.HasConfig;
    }

    private static bool HasBundledConfigChanged(
        BundledConfigSnapshot previousSnapshot,
        BundledConfigSnapshot nextSnapshot)
    {
        if (previousSnapshot.HasConfig != nextSnapshot.HasConfig)
        {
            return true;
        }

        if (!previousSnapshot.HasConfig)
        {
            return false;
        }

        return previousSnapshot.Source != nextSnapshot.Source ||
               !string.Equals(previousSnapshot.Fingerprint, nextSnapshot.Fingerprint, StringComparison.Ordinal);
    }

    private static HostConnectionSource ResolveConfigChangeSource(
        BundledConfigSnapshot previousSnapshot,
        BundledConfigSnapshot nextSnapshot)
    {
        if (nextSnapshot.Source != HostConnectionSource.None)
        {
            return nextSnapshot.Source;
        }

        if (previousSnapshot.Source != HostConnectionSource.None)
        {
            return previousSnapshot.Source;
        }

        return HostConnectionSource.BundledConfig;
    }

    private static string DescribeConfigChange(
        BundledConfigSnapshot previousSnapshot,
        BundledConfigSnapshot nextSnapshot,
        bool configChanged)
    {
        if (!configChanged)
        {
            return nextSnapshot.HasConfig
                ? "Ansight host config is unchanged."
                : "No Ansight host config is available.";
        }

        if (!previousSnapshot.HasConfig && nextSnapshot.HasConfig)
        {
            return "Ansight host config is now available.";
        }

        if (previousSnapshot.HasConfig && !nextSnapshot.HasConfig)
        {
            return "Ansight host config is no longer available.";
        }

        return "Ansight host config changed.";
    }

    private HostConnectionCapabilities UpdateStatusAndCapabilities(
        bool nextHasBundledConfig,
        bool forceChangedEvent = false)
    {
        EventHandler<HostConnectionChangedEventArgs>? statusChanged;
        HostConnectionChangedEventArgs? args = null;

        lock (statusGate)
        {
            hasBundledConfig = nextHasBundledConfig;
            var nextStatus = BuildStatusSnapshot(nextHasBundledConfig);
            var nextCapabilities = BuildCapabilities(nextHasBundledConfig);
            if (!forceChangedEvent && Equals(status, nextStatus) && Equals(capabilities, nextCapabilities))
            {
                return capabilities;
            }

            status = nextStatus;
            capabilities = nextCapabilities;
            statusChanged = StatusChanged;
            args = new HostConnectionChangedEventArgs(status, capabilities);
        }

        statusChanged?.Invoke(this, args);
        return Capabilities;
    }

    private HostConnectionStatus BuildStatusSnapshot(bool nextHasBundledConfig)
    {
        if (!isRuntimeActive())
        {
            return new HostConnectionStatus(
                IsRuntimeActive: false,
                IsConnected: hostConnection.IsConnected,
                ConnectionState: hostConnection.State,
                HasCachedSession: hostConnection.HasCachedProfile,
                HasSavedConfig: HasSavedConfig,
                HasBundledConfig: nextHasBundledConfig,
                SummaryKind: HostConnectionSummaryKind.RuntimeInactive,
                SummaryMessage: "Activate Ansight before connecting to Ansight host.");
        }

        if (hostConnection.State == HostConnectionState.Connecting)
        {
            return new HostConnectionStatus(
                true,
                hostConnection.IsConnected,
                hostConnection.State,
                hostConnection.HasCachedProfile,
                HasSavedConfig,
                nextHasBundledConfig,
                HostConnectionSummaryKind.Connecting,
                hostConnection.StatusSummary);
        }

        if (hostConnection.State == HostConnectionState.Connected)
        {
            return new HostConnectionStatus(
                true,
                hostConnection.IsConnected,
                hostConnection.State,
                hostConnection.HasCachedProfile,
                HasSavedConfig,
                nextHasBundledConfig,
                HostConnectionSummaryKind.Connected,
                hostConnection.StatusSummary);
        }

        var availableSources = 0;
        if (hostConnection.HasCachedProfile)
        {
            availableSources++;
        }

        if (HasSavedConfig)
        {
            availableSources++;
        }

        if (nextHasBundledConfig)
        {
            availableSources++;
        }

        var (summaryKind, summaryMessage) = availableSources switch
        {
            0 => (HostConnectionSummaryKind.DisconnectedNoConfigs, "No Ansight host configs are available."),
            > 1 => (HostConnectionSummaryKind.DisconnectedMultipleConfigsAvailable, "Multiple Ansight host configs are available."),
            _ when hostConnection.HasCachedProfile => (HostConnectionSummaryKind.DisconnectedCachedSessionAvailable, "A cached Ansight host session is available."),
            _ when HasSavedConfig => (HostConnectionSummaryKind.DisconnectedSavedConfigAvailable, "A saved Ansight host config is available."),
            _ => (HostConnectionSummaryKind.DisconnectedBundledConfigAvailable, "A bundled Ansight host config is available.")
        };

        return new HostConnectionStatus(
            true,
            hostConnection.IsConnected,
            hostConnection.State,
            hostConnection.HasCachedProfile,
            HasSavedConfig,
            nextHasBundledConfig,
            summaryKind,
            summaryMessage);
    }

    private HostConnectionCapabilities BuildCapabilities(bool nextHasBundledConfig)
    {
        var runtimeIsActive = isRuntimeActive();
        return new HostConnectionCapabilities(
            CanConnectUsingSavedConfig: runtimeIsActive && HasSavedConfig,
            CanConnectUsingBundledConfig: runtimeIsActive && nextHasBundledConfig,
            CanChooseConfigFile: runtimeIsActive && CanReadRequest(HostConnectionRequestKind.File),
            CanScanConfigQrCode: runtimeIsActive && CanReadRequest(HostConnectionRequestKind.QrCode),
            CanClearSavedConfigs: !hostConnection.IsConnected && (hostConnection.HasCachedProfile || HasSavedConfig));
    }

    private sealed record ResolvedPairingDocument(
        bool Success,
        ParsedPairingDocument? Document,
        string Message,
        HostConnectionSource Source)
    {
        public static ResolvedPairingDocument FromFailure(string message)
            => new(false, null, message, HostConnectionSource.None);

        public static ResolvedPairingDocument FromSuccess(
            ParsedPairingDocument document,
            string message,
            HostConnectionSource source)
            => new(true, document, message, source);
    }

    private sealed record BundledConfigSnapshot(
        bool HasConfig,
        string? Fingerprint,
        HostConnectionSource Source,
        string? Error)
    {
        public static BundledConfigSnapshot None { get; } = new(
            false,
            null,
            HostConnectionSource.None,
            null);

        public bool IsFailure => !HasConfig && !string.IsNullOrWhiteSpace(Error);

        public static BundledConfigSnapshot Unavailable(HostConnectionSource source)
            => new(false, null, source, null);

        public static BundledConfigSnapshot FromFailure(HostConnectionSource source, string error)
            => new(false, null, source, string.IsNullOrWhiteSpace(error) ? "Invalid Ansight host config." : error.Trim());

        public static BundledConfigSnapshot FromDocument(ParsedPairingDocument document, HostConnectionSource source)
            => new(true, CreateFingerprint(document), source, null);

        private static string CreateFingerprint(ParsedPairingDocument document)
        {
            var configDocument = PairingConfigDocumentService.CreateConfigDocument(document);
            var json = PairingConfigDocumentJson.Serialize(configDocument, indented: false);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }
    }
}
