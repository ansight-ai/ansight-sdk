using System.Reflection;
using System.Text.Json;
using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight;

internal sealed class HostPairingManager : IStudioConnection, IDisposable
{
    private static readonly HashSet<string> StoredProfileResetReasonCodes = new(StringComparer.Ordinal)
    {
        PairingFailureCodes.PairingRequired,
        PairingFailureCodes.PairingTokenInvalid,
        PairingFailureCodes.PairingTokenExpired,
        PairingFailureCodes.PairingProofInvalid
    };

    private readonly IHostConnection hostConnection;
    private readonly StudioConnectionOptions options;
    private readonly StoredHostPairingProfileStore preferredProfileStore;
    private readonly PairingConfigDocumentService pairingDocumentService = new();
    private readonly Func<bool> isRuntimeActive;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Lock statusGate = new();
    private StudioConnectionStatus status;
    private StudioConnectionCapabilities capabilities;
    private bool hasBundledProfile;
    private bool disposed;

    internal HostPairingManager(
        IHostConnection hostConnection,
        StudioConnectionOptions options,
        StoredHostPairingProfileStore? preferredProfileStore = null,
        Func<bool>? isRuntimeActive = null)
    {
        this.hostConnection = hostConnection ?? throw new ArgumentNullException(nameof(hostConnection));
        this.options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        this.preferredProfileStore = preferredProfileStore
                                     ?? new StoredHostPairingProfileStore(
                                         StoredPairingDocumentCache.ResolveCacheKey(AutomaticDeviceAppProfileProvider.Instance),
                                         this.options.SavedTicketPath);
        this.isRuntimeActive = isRuntimeActive ?? (() => Runtime.IsActive);
        hasBundledProfile = false;
        status = BuildStatusSnapshot(hasBundledProfile);
        capabilities = BuildCapabilities(hasBundledProfile);
        this.hostConnection.StatusChanged += HandleHostConnectionStatusChanged;
        UpdateStatusAndCapabilities(hasBundledProfile);
    }

    public bool HasSavedTicket => preferredProfileStore.HasStoredDocument;

    public bool IsConnected => hostConnection.IsConnected;

    public StudioConnectionStatus Status
    {
        get
        {
            lock (statusGate)
            {
                return status;
            }
        }
    }

    public StudioConnectionCapabilities Capabilities
    {
        get
        {
            lock (statusGate)
            {
                return capabilities;
            }
        }
    }

    public event EventHandler<StudioConnectionChangedEventArgs>? StatusChanged;

    public async Task<StudioConnectionCapabilities> RefreshCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        var resolvedHasBundledTicket = await ResolveBundledProfileAvailabilityAsync(cancellationToken);
        return UpdateStatusAndCapabilities(resolvedHasBundledTicket);
    }

    private bool CanReadRequest(StudioConnectionRequestKind kind)
    {
        if (options.TicketReader is null)
        {
            return false;
        }

        try
        {
            return options.TicketReader.CanRead(kind);
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to resolve ticket reader support for {kind}: {ex.Message}");
            return false;
        }
    }

    public bool TryParseTicket(string payload, out PairingTicket? ticket, out string error)
    {
        if (!pairingDocumentService.TryParseTicket(payload, out var parsedTicket, out error) || parsedTicket is null)
        {
            ticket = null;
            return false;
        }

        var resolvedTicket = ResolveTicket(parsedTicket, StudioConnectionSource.Payload, "Using supplied pairing ticket.");
        if (!resolvedTicket.Success || resolvedTicket.Document is null)
        {
            ticket = null;
            error = resolvedTicket.Message;
            return false;
        }

        ticket = PairingConfigDocumentService.CreateTicket(resolvedTicket.Document);
        error = string.Empty;
        return true;
    }

    public async Task<StudioConnectionResult> ConnectAsync(
        StudioConnectionRequest? request = null,
        string? clientName = null,
        IProgress<StudioConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        request ??= StudioConnectionRequest.Auto();

        return request.Kind switch
        {
            StudioConnectionRequestKind.Auto => await ConnectAutoAsync(clientName, progress, cancellationToken),
            StudioConnectionRequestKind.SavedTicket => await ConnectUsingSavedTicketAsync(clientName, progress, cancellationToken),
            StudioConnectionRequestKind.BundledTicket => await ConnectUsingBundledTicketAsync(clientName, progress, cancellationToken),
            StudioConnectionRequestKind.Ticket => await ConnectTicketAsync(request.Ticket, clientName, progress, cancellationToken),
            StudioConnectionRequestKind.Payload => await ConnectFromPayloadAsync(
                request.Payload,
                request.SourceDescription,
                clientName,
                progress,
                cancellationToken),
            StudioConnectionRequestKind.File or StudioConnectionRequestKind.QrCode => await ConnectFromReaderAsync(
                request,
                clientName,
                progress,
                cancellationToken),
            _ => StudioConnectionResult.FromFailure(
                $"Unsupported Studio connection request kind '{request.Kind}'.",
                StudioConnectionActionKind.Connect)
        };
    }

    private async Task<StudioConnectionResult> ConnectAutoAsync(
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return StudioConnectionResult.FromSuccess(
                    hostConnection.StatusSummary,
                    StudioConnectionActionKind.AutoConnect,
                    StudioConnectionSource.HostConnection);
            }

            if (hostConnection.HasCachedProfile)
            {
                var cachedProfileResult = await hostConnection.ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);
                if (cachedProfileResult.Success)
                {
                    return ToPairingResult(cachedProfileResult, StudioConnectionActionKind.AutoConnect);
                }
            }

            if (HasSavedTicket)
            {
                return await ConnectUsingPreferredProfileCoreAsync(
                    clientName,
                    progress,
                    cancellationToken,
                    StudioConnectionActionKind.AutoConnect,
                    allowBundledRetry: true);
            }

            return await ConnectUsingBundledProfileCoreAsync(
                clientName,
                progress,
                cancellationToken,
                StudioConnectionActionKind.AutoConnect);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    private async Task<StudioConnectionResult> ConnectTicketAsync(
        PairingTicket? ticket,
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var resolvedDocument = ResolveTicket(ticket, StudioConnectionSource.Payload, "Using supplied pairing ticket.");
            if (!resolvedDocument.Success || resolvedDocument.Document is null)
            {
                return StudioConnectionResult.FromFailure(
                    resolvedDocument.Message,
                    StudioConnectionActionKind.Connect,
                    resolvedDocument.Source);
            }

            var connectResult = await ConnectResolvedDocumentAsync(
                resolvedDocument,
                clientName,
                progress,
                cancellationToken,
                StudioConnectionActionKind.Connect);
            return ToPairingResult(connectResult, StudioConnectionActionKind.Connect);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    private async Task<StudioConnectionResult> ConnectUsingSavedTicketAsync(
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return StudioConnectionResult.FromSuccess(
                    hostConnection.StatusSummary,
                    StudioConnectionActionKind.ConnectUsingSavedTicket,
                    StudioConnectionSource.HostConnection);
            }

            if (hostConnection.HasCachedProfile)
            {
                var cachedProfileResult = await hostConnection.ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);
                if (cachedProfileResult.Success)
                {
                    return ToPairingResult(cachedProfileResult, StudioConnectionActionKind.ConnectUsingSavedTicket);
                }
            }

            if (!HasSavedTicket)
            {
                return StudioConnectionResult.FromFailure(
                    "No saved Ansight pairing ticket is available.",
                    StudioConnectionActionKind.ConnectUsingSavedTicket,
                    StudioConnectionSource.SavedTicket);
            }

            return await ConnectUsingPreferredProfileCoreAsync(
                clientName,
                progress,
                cancellationToken,
                StudioConnectionActionKind.ConnectUsingSavedTicket,
                allowBundledRetry: true);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    private async Task<StudioConnectionResult> ConnectUsingBundledTicketAsync(
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectUsingBundledProfileCoreAsync(
                clientName,
                progress,
                cancellationToken,
                StudioConnectionActionKind.ConnectUsingBundledTicket);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    private async Task<StudioConnectionResult> ConnectFromPayloadAsync(
        string? payload,
        string? sourceDescription,
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return StudioConnectionResult.FromFailure(
                "Paste or load a pairing ticket.",
                StudioConnectionActionKind.ConnectFromPayload,
                StudioConnectionSource.Payload);
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

    private async Task<StudioConnectionResult> ConnectFromReaderAsync(
        StudioConnectionRequest request,
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (options.TicketReader is null || !CanReadRequest(request.Kind))
        {
            return StudioConnectionResult.FromFailure(
                $"No Studio ticket reader is registered for {request.Kind}.",
                StudioConnectionActionKind.ConnectFromPayload,
                StudioConnectionSource.TicketReader);
        }

        string? payload;
        try
        {
            payload = await options.TicketReader.ReadTicketPayloadAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return StudioConnectionResult.FromFailure(
                $"Failed to read a pairing ticket: {ex.Message}",
                StudioConnectionActionKind.ConnectFromPayload,
                StudioConnectionSource.TicketReader);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return StudioConnectionResult.FromFailure(
                "No pairing ticket was provided.",
                StudioConnectionActionKind.ConnectFromPayload,
                StudioConnectionSource.TicketReader);
        }

        return await ConnectFromPayloadAsync(
            payload,
            request.SourceDescription,
            clientName,
            progress,
            cancellationToken);
    }

    public async Task<StudioConnectionResult> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await hostConnection.DisconnectAsync(cancellationToken);
            return ToPairingResult(result, StudioConnectionActionKind.Disconnect);
        }
        finally
        {
            UpdateStatusAndCapabilities(hasBundledProfile);
            operationGate.Release();
        }
    }

    public StudioConnectionResult ClearSavedTickets()
    {
        operationGate.Wait();
        try
        {
            if (hostConnection.IsConnected)
            {
                return StudioConnectionResult.FromFailure(
                    "Disconnect from Ansight Studio before clearing saved tickets.",
                    StudioConnectionActionKind.ClearSavedTickets,
                    StudioConnectionSource.SavedTicket);
            }

            preferredProfileStore.Clear();
            var cachedProfileResult = hostConnection.ClearCachedProfile();
            if (!cachedProfileResult.Success)
            {
                return StudioConnectionResult.FromFailure(
                    cachedProfileResult.Message,
                    StudioConnectionActionKind.ClearSavedTickets,
                    cachedProfileResult.Source,
                    cachedProfileResult.ReasonCode);
            }

            return StudioConnectionResult.FromSuccess(
                "Cleared saved Ansight Studio tickets.",
                StudioConnectionActionKind.ClearSavedTickets,
                StudioConnectionSource.SavedTicket);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            return StudioConnectionResult.FromFailure(
                $"Failed to clear saved Ansight Studio tickets: {ex.Message}",
                StudioConnectionActionKind.ClearSavedTickets,
                StudioConnectionSource.SavedTicket);
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

    internal async Task HandleRuntimeActivatedAsync(CancellationToken cancellationToken = default)
    {
        if (disposed || hostConnection.IsConnected || !isRuntimeActive())
        {
            return;
        }

        var hasBundledDeveloperTicket = await TryResolveBundledProfileAvailabilityAsync(
            ResolveBundledDocumentLoader(StudioConnectionSource.BundledDeveloperTicket),
            StudioConnectionSource.BundledDeveloperTicket,
            cancellationToken);
        if (!hasBundledDeveloperTicket || disposed || !isRuntimeActive())
        {
            return;
        }

        UpdateStatusAndCapabilities(hasBundledProfile || hasBundledDeveloperTicket);
        Logger.Info("Bundled developer pairing ticket detected. Attempting Ansight startup auto-connect.");

        var result = await ConnectAutoAsync(clientName: null, progress: null, cancellationToken: cancellationToken);
        if (result.Success)
        {
            Logger.Info($"Ansight startup auto-connect succeeded. {result.Message}");
            return;
        }

        Logger.Warning($"Ansight startup auto-connect failed. {result.Message}");
    }

    private async Task<StudioConnectionResult> ConnectFromPayloadCoreAsync(
        string payload,
        string? sourceDescription,
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
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
            return StudioConnectionResult.FromFailure(
                resolvedDocument.Message,
                StudioConnectionActionKind.ConnectFromPayload,
                resolvedDocument.Source);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            resolvedDocument,
            clientName,
            progress,
            cancellationToken,
            StudioConnectionActionKind.ConnectFromPayload);
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

        return ToPairingResult(connectResult, StudioConnectionActionKind.ConnectFromPayload);
    }

    private async Task<StudioConnectionResult> ConnectUsingPreferredProfileCoreAsync(
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken,
        StudioConnectionActionKind actionKind,
        bool allowBundledRetry)
    {
        var preferredDocument = await TryResolvePreferredPairingDocumentAsync();
        if (!preferredDocument.Success || preferredDocument.Document is null)
        {
            return StudioConnectionResult.FromFailure(
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
            var bundledResult = await ConnectUsingBundledProfileCoreAsync(clientName, progress, cancellationToken, actionKind);
            return bundledResult.Success || bundledResult.Source != StudioConnectionSource.None
                ? bundledResult
                : ToPairingResult(connectResult, actionKind);
        }

        return ToPairingResult(connectResult, actionKind);
    }

    private async Task<StudioConnectionResult> ConnectUsingBundledProfileCoreAsync(
        string? clientName,
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken,
        StudioConnectionActionKind actionKind)
    {
        var bundledDocument = await TryResolveBundledPairingDocumentAsync(cancellationToken);
        if (!bundledDocument.Success || bundledDocument.Document is null)
        {
            return StudioConnectionResult.FromFailure(
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
        bool preferPreferredProfiles,
        CancellationToken cancellationToken)
    {
        _ = preferPreferredProfiles;
        _ = cancellationToken;

        if (PairingTicketCodeGenerator.TryParse(payload, out var compactTicket) && compactTicket is not null)
        {
            return Task.FromResult(ResolveTicket(
                compactTicket,
                StudioConnectionSource.Payload,
                $"Loaded {sourceDescription ?? "pairing ticket code"}."));
        }

        if (!pairingDocumentService.TryParseTicket(payload, out var ticket, out var error) || ticket is null)
        {
            return Task.FromResult(ResolvedPairingDocument.FromFailure(
                string.IsNullOrWhiteSpace(error)
                    ? "Pairing payloads must be pairing tickets or compact pairing ticket codes."
                    : error));
        }

        return Task.FromResult(ResolveTicket(
            ticket,
            StudioConnectionSource.Payload,
            $"Loaded {sourceDescription ?? "pairing ticket"}."));
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
                ? "Saved pairing ticket is invalid and was cleared."
                : $"{error} Saved pairing ticket was cleared.";
            return Task.FromResult(ResolvedPairingDocument.FromFailure(clearedError));
        }

        var preferredDocument = PairingSessionClient.CreatePreferredDocument(document);
        if (HasStoredHostAddress(document))
        {
            try
            {
                preferredProfileStore.Save(preferredDocument);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to rewrite the saved Ansight pairing ticket without a remembered host address: {ex.Message}");
            }
        }

        return Task.FromResult(ResolvedPairingDocument.FromSuccess(
            preferredDocument,
            "Using saved pairing ticket.",
            StudioConnectionSource.SavedTicket));
    }

    private async Task<ResolvedPairingDocument> TryResolveBundledPairingDocumentAsync(CancellationToken cancellationToken)
    {
        var bundledDeveloperDocument = await TryLoadBundledDocumentAsync(
            ResolveBundledDocumentLoader(StudioConnectionSource.BundledDeveloperTicket),
            "Using bundled developer pairing ticket.",
            StudioConnectionSource.BundledDeveloperTicket,
            cancellationToken);
        if (bundledDeveloperDocument.Success)
        {
            return bundledDeveloperDocument;
        }

        var bundledDocument = await TryLoadBundledDocumentAsync(
            ResolveBundledDocumentLoader(StudioConnectionSource.BundledTicket),
            "Using bundled pairing ticket.",
            StudioConnectionSource.BundledTicket,
            cancellationToken);
        if (bundledDocument.Success)
        {
            return bundledDocument;
        }

        return ResolvedPairingDocument.FromFailure("No bundled pairing ticket is available.");
    }

    private async Task<ResolvedPairingDocument> TryLoadBundledDocumentAsync(
        Func<CancellationToken, Task<string?>>? loader,
        string successMessage,
        StudioConnectionSource source,
        CancellationToken cancellationToken)
    {
        if (loader is null)
        {
            return ResolvedPairingDocument.FromFailure("No bundled pairing ticket is available.");
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
            return ResolvedPairingDocument.FromFailure($"Failed to load a bundled pairing ticket: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return ResolvedPairingDocument.FromFailure("No bundled pairing ticket is available.");
        }

        if (!hostConnection.TryParseAndValidateDocument(json, out var document, out var error) || document is null)
        {
            Logger.Warning($"Ignoring invalid bundled pairing ticket. {error}");
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
        IProgress<StudioConnectionProgressUpdate>? progress,
        CancellationToken cancellationToken,
        StudioConnectionActionKind actionKind)
    {
        ArgumentNullException.ThrowIfNull(resolvedDocument.Document);

        try
        {
            preferredProfileStore.Save(PairingSessionClient.CreatePreferredDocument(resolvedDocument.Document));
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to save the preferred Ansight pairing ticket: {ex.Message}");
        }

        var discoveryPort = ResolveDiscoveryPort(resolvedDocument.Document);
        LogPairingExpectation(resolvedDocument.Document, discoveryPort);

        if (string.IsNullOrWhiteSpace(PairingDiscoveryHintHostAddresses.ResolvePrimary(resolvedDocument.Document.DiscoveryHint)))
        {
            return HostConnectionActionResult.FromFailure(
                "A current Ansight host address is required. Import a fresh pairing ticket or compact pairing code.",
                kind: actionKind,
                source: resolvedDocument.Source,
                reasonCode: PairingFailureCodes.HostAddressRequired);
        }

        var connectResult = await hostConnection.ConnectAsync(
            resolvedDocument.Document,
            clientName,
            new PairingConnectionOptions
            {
                DiscoveryPort = discoveryPort
            },
            progress,
            cancellationToken);
        connectResult = connectResult with
        {
            Kind = actionKind,
            Source = resolvedDocument.Source,
            ReasonCode = connectResult.ReasonCode ?? connectResult.SessionResult?.RejectionCode
        };
        if (!connectResult.Success && resolvedDocument.Source == StudioConnectionSource.SavedTicket)
        {
            var rejectionCode = connectResult.ReasonCode;
            if (!string.IsNullOrWhiteSpace(rejectionCode) &&
                StoredProfileResetReasonCodes.Contains(rejectionCode))
            {
                preferredProfileStore.Clear();
            }
        }

        return connectResult;
    }

    private static StudioConnectionResult ToPairingResult(
        HostConnectionActionResult connectResult,
        StudioConnectionActionKind fallbackKind)
    {
        var actionKind = connectResult.Kind == StudioConnectionActionKind.None ? fallbackKind : connectResult.Kind;
        return connectResult.Success
            ? StudioConnectionResult.FromSuccess(connectResult.Message, actionKind, connectResult.Source, connectResult.ReasonCode)
            : StudioConnectionResult.FromFailure(connectResult.Message, actionKind, connectResult.Source, connectResult.ReasonCode);
    }

    private static bool ShouldRetryWithBundledProfile(
        HostConnectionActionResult connectResult,
        StudioConnectionSource source)
    {
        var rejectionCode = connectResult.ReasonCode;
        return !connectResult.Success &&
               source == StudioConnectionSource.SavedTicket &&
               !string.IsNullOrWhiteSpace(rejectionCode) &&
               (StoredProfileResetReasonCodes.Contains(rejectionCode) ||
                string.Equals(rejectionCode, PairingFailureCodes.HostAddressRequired, StringComparison.Ordinal));
    }

    private async Task<bool> ResolveBundledProfileAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (await TryResolveBundledProfileAvailabilityAsync(
                ResolveBundledDocumentLoader(StudioConnectionSource.BundledDeveloperTicket),
                StudioConnectionSource.BundledDeveloperTicket,
                cancellationToken))
        {
            hasBundledProfile = true;
            return true;
        }

        var hasBundled = await TryResolveBundledProfileAvailabilityAsync(
            ResolveBundledDocumentLoader(StudioConnectionSource.BundledTicket),
            StudioConnectionSource.BundledTicket,
            cancellationToken);
        hasBundledProfile = hasBundled;
        return hasBundled;
    }

    private async Task<bool> TryResolveBundledProfileAvailabilityAsync(
        Func<CancellationToken, Task<string?>>? loader,
        StudioConnectionSource source,
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

    private Func<CancellationToken, Task<string?>>? ResolveBundledDocumentLoader(StudioConnectionSource source)
    {
        return source switch
        {
            StudioConnectionSource.BundledDeveloperTicket => ResolveBundledDocumentLoader(
                options.BundledDeveloperTicketLoader,
                StudioConnectionOptions.BundledDeveloperTicketAssetName),
            StudioConnectionSource.BundledTicket => ResolveBundledDocumentLoader(
                options.BundledTicketLoader,
                StudioConnectionOptions.BundledTicketAssetName),
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

        var bundledTicketAssembly = options.BundledTicketAssembly;
        if (bundledTicketAssembly is null)
        {
            return null;
        }

        return cancellationToken => LoadEmbeddedResourceTextAsync(bundledTicketAssembly, logicalName, cancellationToken);
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

    private int ResolveDiscoveryPort(ParsedPairingDocument document)
    {
        var candidates = new[]
        {
            options.DiscoveryPort,
            document.DiscoveryHint?.DiscoveryPort
        };

        foreach (var candidate in candidates)
        {
            if (candidate is > 0 and <= ushort.MaxValue)
            {
                return candidate.Value;
            }
        }

        return PairingProtocolDefaults.DiscoveryPort;
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

    private static bool HasStoredHostAddress(ParsedPairingDocument document)
    {
        return !string.IsNullOrWhiteSpace(PairingDiscoveryHintHostAddresses.ResolvePrimary(document.DiscoveryHint));
    }

    private static string DescribeSource(StudioConnectionSource source)
    {
        return source switch
        {
            StudioConnectionSource.BundledDeveloperTicket => "the bundled developer pairing ticket",
            StudioConnectionSource.BundledTicket => "the bundled pairing ticket",
            _ => "the bundled ticket source"
        };
    }

    private ResolvedPairingDocument ResolveTicket(
        PairingTicket ticket,
        StudioConnectionSource source,
        string successMessage)
    {
        if (!hostConnection.TryParseAndValidateDocument(
                PairingTicketJson.Serialize(ticket, indented: false),
                out var document,
                out var error) || document is null)
        {
            return ResolvedPairingDocument.FromFailure(error);
        }

        return ResolvedPairingDocument.FromSuccess(document, successMessage, source);
    }

    private void HandleHostConnectionStatusChanged(object? sender, HostConnectionStatusChangedEventArgs e)
    {
        UpdateStatusAndCapabilities(hasBundledProfile);
    }

    private StudioConnectionCapabilities UpdateStatusAndCapabilities(bool nextHasBundledTicket)
    {
        EventHandler<StudioConnectionChangedEventArgs>? statusChanged;
        StudioConnectionChangedEventArgs? args = null;

        lock (statusGate)
        {
            hasBundledProfile = nextHasBundledTicket;
            var nextStatus = BuildStatusSnapshot(nextHasBundledTicket);
            var nextCapabilities = BuildCapabilities(nextHasBundledTicket);
            if (Equals(status, nextStatus) && Equals(capabilities, nextCapabilities))
            {
                return capabilities;
            }

            status = nextStatus;
            capabilities = nextCapabilities;
            statusChanged = StatusChanged;
            args = new StudioConnectionChangedEventArgs(status, capabilities);
        }

        statusChanged?.Invoke(this, args);
        return Capabilities;
    }

    private StudioConnectionStatus BuildStatusSnapshot(bool nextHasBundledTicket)
    {
        if (!isRuntimeActive())
        {
            return new StudioConnectionStatus(
                IsRuntimeActive: false,
                IsConnected: hostConnection.IsConnected,
                ConnectionState: hostConnection.State,
                HasCachedSession: hostConnection.HasCachedProfile,
                HasSavedTicket: HasSavedTicket,
                HasBundledTicket: nextHasBundledTicket,
                SummaryKind: StudioConnectionSummaryKind.RuntimeInactive,
                SummaryMessage: "Activate Ansight before connecting to Ansight Studio.");
        }

        if (hostConnection.State == HostConnectionState.Connecting)
        {
            return new StudioConnectionStatus(
                true,
                hostConnection.IsConnected,
                hostConnection.State,
                hostConnection.HasCachedProfile,
                HasSavedTicket,
                nextHasBundledTicket,
                StudioConnectionSummaryKind.Connecting,
                hostConnection.StatusSummary);
        }

        if (hostConnection.State == HostConnectionState.Connected)
        {
            return new StudioConnectionStatus(
                true,
                hostConnection.IsConnected,
                hostConnection.State,
                hostConnection.HasCachedProfile,
                HasSavedTicket,
                nextHasBundledTicket,
                StudioConnectionSummaryKind.Connected,
                hostConnection.StatusSummary);
        }

        var availableSources = 0;
        if (hostConnection.HasCachedProfile)
        {
            availableSources++;
        }

        if (HasSavedTicket)
        {
            availableSources++;
        }

        if (nextHasBundledTicket)
        {
            availableSources++;
        }

        var (summaryKind, summaryMessage) = availableSources switch
        {
            0 => (StudioConnectionSummaryKind.DisconnectedNoTickets, "No Ansight Studio tickets are available."),
            > 1 => (StudioConnectionSummaryKind.DisconnectedMultipleTicketsAvailable, "Multiple Ansight Studio tickets are available."),
            _ when hostConnection.HasCachedProfile => (StudioConnectionSummaryKind.DisconnectedCachedSessionAvailable, "A cached Ansight Studio session is available."),
            _ when HasSavedTicket => (StudioConnectionSummaryKind.DisconnectedSavedTicketAvailable, "A saved Ansight Studio ticket is available."),
            _ => (StudioConnectionSummaryKind.DisconnectedBundledTicketAvailable, "A bundled Ansight Studio ticket is available.")
        };

        return new StudioConnectionStatus(
            true,
            hostConnection.IsConnected,
            hostConnection.State,
            hostConnection.HasCachedProfile,
            HasSavedTicket,
            nextHasBundledTicket,
            summaryKind,
            summaryMessage);
    }

    private StudioConnectionCapabilities BuildCapabilities(bool nextHasBundledTicket)
    {
        var runtimeIsActive = isRuntimeActive();
        return new StudioConnectionCapabilities(
            CanConnectUsingSavedTicket: runtimeIsActive && (hostConnection.HasCachedProfile || HasSavedTicket),
            CanConnectUsingBundledTicket: runtimeIsActive && nextHasBundledTicket,
            CanChooseTicketFile: runtimeIsActive && CanReadRequest(StudioConnectionRequestKind.File),
            CanScanTicketQrCode: runtimeIsActive && CanReadRequest(StudioConnectionRequestKind.QrCode),
            CanClearSavedTickets: !hostConnection.IsConnected && (hostConnection.HasCachedProfile || HasSavedTicket));
    }

    private sealed record ResolvedPairingDocument(
        bool Success,
        ParsedPairingDocument? Document,
        string Message,
        StudioConnectionSource Source)
    {
        public static ResolvedPairingDocument FromFailure(string message)
            => new(false, null, message, StudioConnectionSource.None);

        public static ResolvedPairingDocument FromSuccess(
            ParsedPairingDocument document,
            string message,
            StudioConnectionSource source)
            => new(true, document, message, source);
    }
}
