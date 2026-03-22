using System.Reflection;
using System.Text.Json;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class HostPairingManager : IHostPairing, IDisposable
{
    private static readonly HashSet<string> StoredProfileResetReasonCodes = new(StringComparer.Ordinal)
    {
        "PairingRequired",
        "PairingTokenInvalid",
        "PairingTokenExpired",
        "PairingProofInvalid"
    };

    private readonly IHostConnection hostConnection;
    private readonly HostPairingOptions options;
    private readonly StoredHostPairingProfileStore preferredProfileStore;
    private readonly Func<bool> isRuntimeActive;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Lock statusGate = new();
    private HostPairingStatusSnapshot status;
    private HostPairingCapabilities capabilities;
    private bool hasBundledProfile;
    private bool disposed;

    internal HostPairingManager(
        IHostConnection hostConnection,
        HostPairingOptions options,
        StoredHostPairingProfileStore? preferredProfileStore = null,
        Func<bool>? isRuntimeActive = null)
    {
        this.hostConnection = hostConnection ?? throw new ArgumentNullException(nameof(hostConnection));
        this.options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        this.preferredProfileStore = preferredProfileStore
                                     ?? new StoredHostPairingProfileStore(
                                         StoredPairingDocumentCache.ResolveCacheKey(AutomaticDeviceAppProfileProvider.Instance),
                                         this.options.PreferredProfilePath);
        this.isRuntimeActive = isRuntimeActive ?? (() => Runtime.IsActive);
        hasBundledProfile = false;
        status = BuildStatusSnapshot(hasBundledProfile);
        capabilities = BuildCapabilities(hasBundledProfile);
        this.hostConnection.StatusChanged += HandleHostConnectionStatusChanged;
        UpdateStatusAndCapabilities(hasBundledProfile);
    }

    public bool HasPreferredProfile => preferredProfileStore.HasStoredDocument;

    public bool IsConnected => hostConnection.IsConnected;

    public HostPairingStatusSnapshot Status
    {
        get
        {
            lock (statusGate)
            {
                return status;
            }
        }
    }

    public HostPairingCapabilities Capabilities
    {
        get
        {
            lock (statusGate)
            {
                return capabilities;
            }
        }
    }

    public event EventHandler<HostPairingStatusChangedEventArgs>? StatusChanged;

    public async Task<HostPairingCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var resolvedHasBundledProfile = await ResolveBundledProfileAvailabilityAsync(cancellationToken);
        return UpdateStatusAndCapabilities(resolvedHasBundledProfile);
    }

    public bool CanReadPayload(HostPairingPayloadReadKind kind)
    {
        if (options.PayloadReader is null)
        {
            return false;
        }

        try
        {
            return options.PayloadReader.CanRead(kind);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to resolve payload reader support for {kind}: {ex.Message}");
            return false;
        }
    }

    public async Task<HostPairingActionResult> AutoConnectAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostPairingActionResult.FromSuccess(
                    hostConnection.StatusSummary,
                    HostPairingActionKind.AutoConnect,
                    HostPairingSource.HostConnection);
            }

            if (hostConnection.HasCachedProfile)
            {
                var cachedProfileResult = await hostConnection.ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);
                if (cachedProfileResult.Success)
                {
                    return ToPairingResult(cachedProfileResult, HostPairingActionKind.AutoConnect);
                }
            }

            if (HasPreferredProfile)
            {
                return await ConnectUsingPreferredProfileCoreAsync(
                    clientName,
                    progress,
                    cancellationToken,
                    HostPairingActionKind.AutoConnect,
                    allowBundledRetry: true);
            }

            return await ConnectUsingBundledProfileCoreAsync(
                clientName,
                progress,
                cancellationToken,
                HostPairingActionKind.AutoConnect);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    public async Task<HostPairingActionResult> ConnectUsingStoredProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostPairingActionResult.FromSuccess(
                    hostConnection.StatusSummary,
                    HostPairingActionKind.ConnectUsingStoredProfile,
                    HostPairingSource.HostConnection);
            }

            if (hostConnection.HasCachedProfile)
            {
                var cachedProfileResult = await hostConnection.ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);
                if (cachedProfileResult.Success)
                {
                    return ToPairingResult(cachedProfileResult, HostPairingActionKind.ConnectUsingStoredProfile);
                }
            }

            if (!HasPreferredProfile)
            {
                return HostPairingActionResult.FromFailure(
                    "No saved Ansight pairing profile is available.",
                    HostPairingActionKind.ConnectUsingStoredProfile,
                    HostPairingSource.StoredProfile);
            }

            return await ConnectUsingPreferredProfileCoreAsync(
                clientName,
                progress,
                cancellationToken,
                HostPairingActionKind.ConnectUsingStoredProfile,
                allowBundledRetry: true);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    public async Task<HostPairingActionResult> ConnectUsingBundledProfileAsync(
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectUsingBundledProfileCoreAsync(
                clientName,
                progress,
                cancellationToken,
                HostPairingActionKind.ConnectUsingBundledProfile);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    public async Task<HostPairingActionResult> ConnectFromPayloadAsync(
        string payload,
        string? sourceDescription = null,
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return HostPairingActionResult.FromFailure(
                "Paste or load a pairing config.",
                HostPairingActionKind.ConnectFromPayload,
                HostPairingSource.Payload);
        }

        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectFromPayloadCoreAsync(
                payload,
                sourceDescription,
                clientName,
                progress,
                cancellationToken,
                preferPreferredProfiles: true);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    public async Task<HostPairingActionResult> ConnectFromPayloadReaderAsync(
        HostPairingPayloadReadRequest request,
        string? clientName = null,
        IProgress<HostPairingProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (options.PayloadReader is null || !CanReadPayload(request.Kind))
        {
            return HostPairingActionResult.FromFailure(
                $"No host pairing payload reader is registered for {request.Kind}.",
                HostPairingActionKind.ConnectFromPayload,
                HostPairingSource.PayloadReader);
        }

        string? payload;
        try
        {
            payload = await options.PayloadReader.ReadPayloadAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HostPairingActionResult.FromFailure(
                $"Failed to read a pairing payload: {ex.Message}",
                HostPairingActionKind.ConnectFromPayload,
                HostPairingSource.PayloadReader);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return HostPairingActionResult.FromFailure(
                "No pairing payload was provided.",
                HostPairingActionKind.ConnectFromPayload,
                HostPairingSource.PayloadReader);
        }

        return await ConnectFromPayloadAsync(
            payload,
            request.SourceDescription,
            clientName,
            progress,
            cancellationToken);
    }

    public async Task<HostPairingActionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await hostConnection.DisconnectAsync(cancellationToken);
            return ToPairingResult(result, HostPairingActionKind.Disconnect);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    public HostPairingActionResult ClearStoredProfiles()
    {
        operationGate.Wait();
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostPairingActionResult.FromFailure(
                    "Disconnect from the Ansight host before clearing pairing profiles.",
                    HostPairingActionKind.ClearStoredProfiles,
                    HostPairingSource.StoredProfile);
            }

            preferredProfileStore.Clear();
            var cachedProfileResult = hostConnection.ClearCachedProfile();
            if (!cachedProfileResult.Success)
            {
                return HostPairingActionResult.FromFailure(
                    cachedProfileResult.Message,
                    HostPairingActionKind.ClearStoredProfiles,
                    cachedProfileResult.Source,
                    cachedProfileResult.ReasonCode);
            }

            return HostPairingActionResult.FromSuccess(
                "Cleared stored Ansight pairing profiles.",
                HostPairingActionKind.ClearStoredProfiles,
                HostPairingSource.StoredProfile);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            return HostPairingActionResult.FromFailure(
                $"Failed to clear stored Ansight pairing profiles: {ex.Message}",
                HostPairingActionKind.ClearStoredProfiles,
                HostPairingSource.StoredProfile);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
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

    private async Task<HostPairingActionResult> ConnectFromPayloadCoreAsync(
        string payload,
        string? sourceDescription,
        string? clientName,
        IProgress<HostPairingProgressUpdate>? progress,
        CancellationToken cancellationToken,
        bool preferPreferredProfiles)
    {
        var resolvedDocument = await ResolvePairingDocumentAsync(
            payload,
            sourceDescription,
            preferPreferredProfiles,
            cancellationToken);
        if (!resolvedDocument.Success || resolvedDocument.Document is null)
        {
            return HostPairingActionResult.FromFailure(
                resolvedDocument.Message,
                HostPairingActionKind.ConnectFromPayload,
                resolvedDocument.Source);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            resolvedDocument,
            clientName,
            progress,
            cancellationToken,
            HostPairingActionKind.ConnectFromPayload);
        if (ShouldRetryWithBundledProfile(connectResult, resolvedDocument.Source) && preferPreferredProfiles)
        {
            return await ConnectFromPayloadCoreAsync(
                payload,
                sourceDescription,
                clientName,
                progress,
                cancellationToken,
                preferPreferredProfiles: false);
        }

        return ToPairingResult(connectResult, HostPairingActionKind.ConnectFromPayload);
    }

    private async Task<HostPairingActionResult> ConnectUsingPreferredProfileCoreAsync(
        string? clientName,
        IProgress<HostPairingProgressUpdate>? progress,
        CancellationToken cancellationToken,
        HostPairingActionKind actionKind,
        bool allowBundledRetry)
    {
        var preferredDocument = await TryResolvePreferredPairingDocumentAsync();
        if (!preferredDocument.Success || preferredDocument.Document is null)
        {
            return HostPairingActionResult.FromFailure(
                preferredDocument.Message,
                actionKind,
                preferredDocument.Source);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            preferredDocument,
            clientName,
            progress,
            cancellationToken,
            actionKind);
        if (allowBundledRetry && ShouldRetryWithBundledProfile(connectResult, preferredDocument.Source))
        {
            return await ConnectUsingBundledProfileCoreAsync(clientName, progress, cancellationToken, actionKind);
        }

        return ToPairingResult(connectResult, actionKind);
    }

    private async Task<HostPairingActionResult> ConnectUsingBundledProfileCoreAsync(
        string? clientName,
        IProgress<HostPairingProgressUpdate>? progress,
        CancellationToken cancellationToken,
        HostPairingActionKind actionKind)
    {
        var bundledDocument = await TryResolveBundledPairingDocumentAsync(cancellationToken);
        if (!bundledDocument.Success || bundledDocument.Document is null)
        {
            return HostPairingActionResult.FromFailure(
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

    private async Task<ResolvedPairingDocument> ResolvePairingDocumentAsync(
        string payload,
        string? sourceDescription,
        bool preferPreferredProfiles,
        CancellationToken cancellationToken)
    {
        if (QrDiscoveryPayload.TryParseConnectionPayload(payload, out var connectionPayload))
        {
            var baseDocument = await ResolveBaseDocumentForPayloadAsync(preferPreferredProfiles, cancellationToken);
            if (!baseDocument.Success || baseDocument.Document is null)
            {
                return ResolvedPairingDocument.FromFailure(
                    "QR pairing code requires a saved or bundled pairing profile before it can be used.");
            }

            var bootstrap = new PairingBootstrapDocument
            {
                Schema = PairingBootstrapDocument.SchemaName,
                PairingConfig = baseDocument.Document.TrustAnchorConfig ?? baseDocument.Document.Config,
                Discovery = connectionPayload!.Discovery ?? baseDocument.Document.DiscoveryHint,
                ConnectionHint = connectionPayload.Connection
            };
            var bootstrapJson = JsonSerializer.Serialize(bootstrap, PairingJson.Compact);
            if (!hostConnection.TryParseAndValidateDocument(bootstrapJson, out var bootstrapDocument, out var bootstrapError) ||
                bootstrapDocument is null)
            {
                return ResolvedPairingDocument.FromFailure(bootstrapError);
            }

            return ResolvedPairingDocument.FromSuccess(
                bootstrapDocument,
                $"Loaded {sourceDescription ?? "QR pairing code"}.",
                HostPairingSource.QrConnectionPayload);
        }

        if (QrDiscoveryPayload.TryParse(payload, out var discoveryHint))
        {
            var baseDocument = await ResolveBaseDocumentForPayloadAsync(preferPreferredProfiles, cancellationToken);
            if (!baseDocument.Success || baseDocument.Document is null)
            {
                return ResolvedPairingDocument.FromFailure(
                    "This QR code only contains host discovery metadata. Save or bundle a pairing profile before scanning it.");
            }

            var mergedDocument = new ParsedPairingDocument
            {
                Config = baseDocument.Document.Config,
                DiscoveryHint = discoveryHint,
                TrustAnchorConfig = baseDocument.Document.TrustAnchorConfig,
                ConnectionHint = baseDocument.Document.ConnectionHint
            };
            var mergedJson = PairingDocumentJson.Serialize(mergedDocument);
            if (!hostConnection.TryParseAndValidateDocument(mergedJson, out var validatedDocument, out var validationError) ||
                validatedDocument is null)
            {
                return ResolvedPairingDocument.FromFailure(validationError);
            }

            return ResolvedPairingDocument.FromSuccess(
                validatedDocument,
                $"Loaded {sourceDescription ?? "QR discovery code"}.",
                HostPairingSource.QrDiscoveryPayload);
        }

        if (!hostConnection.TryParseAndValidateDocument(payload, out var document, out var error) || document is null)
        {
            return ResolvedPairingDocument.FromFailure(error);
        }

        return ResolvedPairingDocument.FromSuccess(
            document,
            $"Loaded {sourceDescription ?? "pairing code"}.",
            HostPairingSource.Payload);
    }

    private async Task<ResolvedPairingDocument> ResolveBaseDocumentForPayloadAsync(
        bool preferPreferredProfiles,
        CancellationToken cancellationToken)
    {
        if (preferPreferredProfiles)
        {
            var preferredDocument = await TryResolvePreferredPairingDocumentAsync();
            if (preferredDocument.Success)
            {
                return preferredDocument;
            }
        }

        return await TryResolveBundledPairingDocumentAsync(cancellationToken);
    }

    private Task<ResolvedPairingDocument> TryResolvePreferredPairingDocumentAsync()
    {
        if (!preferredProfileStore.TryLoad(out var json, out var error) || string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult(ResolvedPairingDocument.FromFailure(error));
        }

        if (!hostConnection.TryParseAndValidateDocument(json, out var document, out error) || document is null)
        {
            preferredProfileStore.Clear();
            var clearedError = string.IsNullOrWhiteSpace(error)
                ? "Saved pairing profile is invalid and was cleared."
                : $"{error} Saved pairing profile was cleared.";
            return Task.FromResult(ResolvedPairingDocument.FromFailure(clearedError));
        }

        return Task.FromResult(ResolvedPairingDocument.FromSuccess(
            document,
            "Using saved pairing profile.",
            HostPairingSource.StoredProfile));
    }

    private async Task<ResolvedPairingDocument> TryResolveBundledPairingDocumentAsync(CancellationToken cancellationToken)
    {
        var bundledDeveloperDocument = await TryLoadBundledDocumentAsync(
            ResolveBundledDocumentLoader(HostPairingSource.BundledDeveloperProfile),
            "Using bundled developer pairing config.",
            HostPairingSource.BundledDeveloperProfile,
            cancellationToken);
        if (bundledDeveloperDocument.Success)
        {
            return bundledDeveloperDocument;
        }

        var bundledDocument = await TryLoadBundledDocumentAsync(
            ResolveBundledDocumentLoader(HostPairingSource.BundledProfile),
            "Using bundled pairing config.",
            HostPairingSource.BundledProfile,
            cancellationToken);
        if (bundledDocument.Success)
        {
            return bundledDocument;
        }

        return ResolvedPairingDocument.FromFailure("No bundled pairing profile is available.");
    }

    private async Task<ResolvedPairingDocument> TryLoadBundledDocumentAsync(
        Func<CancellationToken, Task<string?>>? loader,
        string successMessage,
        HostPairingSource source,
        CancellationToken cancellationToken)
    {
        if (loader is null)
        {
            return ResolvedPairingDocument.FromFailure("No bundled pairing profile is available.");
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
            return ResolvedPairingDocument.FromFailure($"Failed to load a bundled pairing profile: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return ResolvedPairingDocument.FromFailure("No bundled pairing profile is available.");
        }

        if (!hostConnection.TryParseAndValidateDocument(json, out var document, out var error) || document is null)
        {
            Logger.Warning($"Ignoring invalid bundled pairing profile. {error}");
            return ResolvedPairingDocument.FromFailure(error);
        }

        return ResolvedPairingDocument.FromSuccess(
            document,
            successMessage,
            source);
    }

    private async Task<HostConnectionActionResult> ConnectResolvedDocumentAsync(
        ResolvedPairingDocument resolvedDocument,
        string? clientName,
        IProgress<HostPairingProgressUpdate>? progress,
        CancellationToken cancellationToken,
        HostPairingActionKind actionKind)
    {
        ArgumentNullException.ThrowIfNull(resolvedDocument.Document);

        try
        {
            preferredProfileStore.Save(resolvedDocument.Document);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to save the preferred Ansight pairing profile: {ex.Message}");
        }

        LogPairingExpectation(resolvedDocument.Document);

        var manualHostAddress = string.IsNullOrWhiteSpace(resolvedDocument.Document.DiscoveryHint?.HostAddress)
            ? null
            : resolvedDocument.Document.DiscoveryHint.HostAddress.Trim();
        var discoveryMode = string.IsNullOrWhiteSpace(manualHostAddress)
            ? PairingDiscoveryMode.ConfiguredHint
            : PairingDiscoveryMode.BasicManual;

        var connectResult = await hostConnection.ConnectAsync(
            resolvedDocument.Document,
            clientName,
            new PairingConnectionOptions
            {
                DiscoveryMode = discoveryMode,
                ManualHostAddress = manualHostAddress
            },
            progress,
            cancellationToken);
        connectResult = connectResult with
        {
            Kind = actionKind,
            Source = resolvedDocument.Source,
            ReasonCode = connectResult.ReasonCode ?? connectResult.SessionResult?.RejectionCode
        };
        if (!connectResult.Success && resolvedDocument.Source == HostPairingSource.StoredProfile)
        {
            var rejectionCode = connectResult.SessionResult?.RejectionCode;
            if (!string.IsNullOrWhiteSpace(rejectionCode) &&
                StoredProfileResetReasonCodes.Contains(rejectionCode))
            {
                preferredProfileStore.Clear();
            }
        }

        return connectResult;
    }

    private static HostPairingActionResult ToPairingResult(
        HostConnectionActionResult connectResult,
        HostPairingActionKind fallbackKind)
    {
        var actionKind = connectResult.Kind == HostPairingActionKind.None ? fallbackKind : connectResult.Kind;
        return connectResult.Success
            ? HostPairingActionResult.FromSuccess(connectResult.Message, actionKind, connectResult.Source, connectResult.ReasonCode)
            : HostPairingActionResult.FromFailure(connectResult.Message, actionKind, connectResult.Source, connectResult.ReasonCode);
    }

    private static bool ShouldRetryWithBundledProfile(
        HostConnectionActionResult connectResult,
        HostPairingSource source)
    {
        var rejectionCode = connectResult.SessionResult?.RejectionCode;
        return !connectResult.Success &&
               source == HostPairingSource.StoredProfile &&
               !string.IsNullOrWhiteSpace(rejectionCode) &&
               StoredProfileResetReasonCodes.Contains(rejectionCode);
    }

    private async Task<bool> ResolveBundledProfileAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (await TryResolveBundledProfileAvailabilityAsync(
                ResolveBundledDocumentLoader(HostPairingSource.BundledDeveloperProfile),
                HostPairingSource.BundledDeveloperProfile,
                cancellationToken))
        {
            hasBundledProfile = true;
            return true;
        }

        var hasBundled = await TryResolveBundledProfileAvailabilityAsync(
            ResolveBundledDocumentLoader(HostPairingSource.BundledProfile),
            HostPairingSource.BundledProfile,
            cancellationToken);
        hasBundledProfile = hasBundled;
        return hasBundled;
    }

    private async Task<bool> TryResolveBundledProfileAvailabilityAsync(
        Func<CancellationToken, Task<string?>>? loader,
        HostPairingSource source,
        CancellationToken cancellationToken)
    {
        if (loader is null)
        {
            return false;
        }

        try
        {
            var json = await loader(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            return hostConnection.TryParseAndValidateDocument(json, out var _, out var _);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to probe {DescribeSource(source)} availability: {ex.Message}");
            return false;
        }
    }

    private Func<CancellationToken, Task<string?>>? ResolveBundledDocumentLoader(HostPairingSource source)
    {
        return source switch
        {
            HostPairingSource.BundledDeveloperProfile => ResolveBundledDocumentLoader(
                options.BundledDeveloperProfileLoader,
                HostPairingOptions.BundledDeveloperAssetName),
            HostPairingSource.BundledProfile => ResolveBundledDocumentLoader(
                options.BundledProfileLoader,
                HostPairingOptions.BundledAssetName),
            _ => null
        };
    }

    private Func<CancellationToken, Task<string?>>? ResolveBundledDocumentLoader(
        Func<CancellationToken, Task<string?>>? explicitLoader,
        string logicalName)
    {
        if (explicitLoader is not null)
        {
            return explicitLoader;
        }

        var bundledProfileAssembly = options.BundledProfileAssembly;
        if (bundledProfileAssembly is null)
        {
            return null;
        }

        return cancellationToken => LoadEmbeddedResourceTextAsync(bundledProfileAssembly, logicalName, cancellationToken);
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

    private static void LogPairingExpectation(ParsedPairingDocument document)
    {
        var expectedHostAddress = FirstNonEmpty(document.DiscoveryHint?.HostAddress);
        var expectedWifiName = FirstNonEmpty(document.DiscoveryHint?.WifiName);
        var expectedHostName = FirstNonEmpty(document.DiscoveryHint?.HostName, document.Config.Host.HostName);
        var discoveryPort = document.Config.Host.DiscoveryPort;

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

    private static string DescribeSource(HostPairingSource source)
    {
        return source switch
        {
            HostPairingSource.BundledDeveloperProfile => "the bundled developer pairing profile",
            HostPairingSource.BundledProfile => "the bundled pairing profile",
            _ => "the bundled pairing source"
        };
    }

    private void HandleHostConnectionStatusChanged(object? sender, HostConnectionStatusChangedEventArgs e)
    {
        UpdateStatusAndCapabilities(hasBundledProfile);
    }

    private HostPairingCapabilities UpdateStatusAndCapabilities(bool nextHasBundledProfile)
    {
        EventHandler<HostPairingStatusChangedEventArgs>? statusChanged;
        HostPairingStatusChangedEventArgs? args = null;

        lock (statusGate)
        {
            hasBundledProfile = nextHasBundledProfile;
            var nextStatus = BuildStatusSnapshot(nextHasBundledProfile);
            var nextCapabilities = BuildCapabilities(nextHasBundledProfile);
            if (Equals(status, nextStatus) && Equals(capabilities, nextCapabilities))
            {
                return capabilities;
            }

            status = nextStatus;
            capabilities = nextCapabilities;
            statusChanged = StatusChanged;
            args = new HostPairingStatusChangedEventArgs(status, capabilities);
        }

        statusChanged?.Invoke(this, args);
        return Capabilities;
    }

    private HostPairingStatusSnapshot BuildStatusSnapshot(bool nextHasBundledProfile)
    {
        if (!isRuntimeActive())
        {
            return new HostPairingStatusSnapshot(
                IsRuntimeActive: false,
                IsConnected: hostConnection.IsConnected,
                ConnectionState: hostConnection.State,
                HasCachedProfile: hostConnection.HasCachedProfile,
                HasPreferredProfile: HasPreferredProfile,
                HasBundledProfile: nextHasBundledProfile,
                SummaryKind: HostPairingSummaryKind.RuntimeInactive,
                SummaryMessage: "Activate Ansight before connecting to a host.");
        }

        if (hostConnection.State == HostConnectionState.Connecting)
        {
            return new HostPairingStatusSnapshot(
                true,
                hostConnection.IsConnected,
                hostConnection.State,
                hostConnection.HasCachedProfile,
                HasPreferredProfile,
                nextHasBundledProfile,
                HostPairingSummaryKind.Connecting,
                hostConnection.StatusSummary);
        }

        if (hostConnection.State == HostConnectionState.Connected)
        {
            return new HostPairingStatusSnapshot(
                true,
                hostConnection.IsConnected,
                hostConnection.State,
                hostConnection.HasCachedProfile,
                HasPreferredProfile,
                nextHasBundledProfile,
                HostPairingSummaryKind.Connected,
                hostConnection.StatusSummary);
        }

        var availableSources = 0;
        if (hostConnection.HasCachedProfile)
        {
            availableSources++;
        }

        if (HasPreferredProfile)
        {
            availableSources++;
        }

        if (nextHasBundledProfile)
        {
            availableSources++;
        }

        var (summaryKind, summaryMessage) = availableSources switch
        {
            0 => (HostPairingSummaryKind.DisconnectedNoProfiles, "No Ansight pairing profiles are available."),
            > 1 => (HostPairingSummaryKind.DisconnectedMultipleProfilesAvailable, "Multiple Ansight pairing profiles are available."),
            _ when hostConnection.HasCachedProfile => (HostPairingSummaryKind.DisconnectedCachedProfileAvailable, "A cached Ansight host pairing profile is available."),
            _ when HasPreferredProfile => (HostPairingSummaryKind.DisconnectedStoredProfileAvailable, "A saved Ansight pairing profile is available."),
            _ => (HostPairingSummaryKind.DisconnectedBundledProfileAvailable, "A bundled Ansight pairing profile is available.")
        };

        return new HostPairingStatusSnapshot(
            true,
            hostConnection.IsConnected,
            hostConnection.State,
            hostConnection.HasCachedProfile,
            HasPreferredProfile,
            nextHasBundledProfile,
            summaryKind,
            summaryMessage);
    }

    private HostPairingCapabilities BuildCapabilities(bool nextHasBundledProfile)
    {
        var runtimeIsActive = isRuntimeActive();
        return new HostPairingCapabilities(
            CanConnectUsingStored: runtimeIsActive && (hostConnection.HasCachedProfile || HasPreferredProfile),
            CanConnectUsingBundled: runtimeIsActive && nextHasBundledProfile,
            CanClearProfiles: !hostConnection.IsConnected && (hostConnection.HasCachedProfile || HasPreferredProfile),
            CanUseQrPayloadWithBaseProfile: runtimeIsActive && (HasPreferredProfile || nextHasBundledProfile));
    }

    private sealed record ResolvedPairingDocument(
        bool Success,
        ParsedPairingDocument? Document,
        string Message,
        HostPairingSource Source)
    {
        public static ResolvedPairingDocument FromFailure(string message)
            => new(false, null, message, HostPairingSource.None);

        public static ResolvedPairingDocument FromSuccess(
            ParsedPairingDocument document,
            string message,
            HostPairingSource source)
            => new(true, document, message, source);
    }
}
