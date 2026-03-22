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
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private bool disposed;

    internal HostPairingManager(
        IHostConnection hostConnection,
        HostPairingOptions options,
        StoredHostPairingProfileStore? preferredProfileStore = null)
    {
        this.hostConnection = hostConnection ?? throw new ArgumentNullException(nameof(hostConnection));
        this.options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        this.preferredProfileStore = preferredProfileStore
                                     ?? new StoredHostPairingProfileStore(
                                         StoredPairingDocumentCache.ResolveCacheKey(AutomaticDeviceAppProfileProvider.Instance),
                                         this.options.PreferredProfilePath);
    }

    public bool HasPreferredProfile => preferredProfileStore.HasStoredDocument;

    public async Task<HostPairingActionResult> AutoConnectAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostPairingActionResult.FromSuccess(hostConnection.StatusSummary);
            }

            if (hostConnection.HasCachedProfile)
            {
                var cachedProfileResult = await hostConnection.ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);
                if (cachedProfileResult.Success)
                {
                    return ToPairingResult(cachedProfileResult);
                }
            }

            if (HasPreferredProfile)
            {
                return await ConnectUsingPreferredProfileCoreAsync(
                    clientName,
                    progress,
                    cancellationToken,
                    allowBundledRetry: true);
            }

            return await ConnectUsingBundledProfileCoreAsync(clientName, progress, cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<HostPairingActionResult> ConnectUsingStoredProfileAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            if (hostConnection.IsConnected)
            {
                return HostPairingActionResult.FromSuccess(hostConnection.StatusSummary);
            }

            if (hostConnection.HasCachedProfile)
            {
                var cachedProfileResult = await hostConnection.ConnectUsingCachedProfileAsync(clientName, progress, cancellationToken);
                if (cachedProfileResult.Success)
                {
                    return ToPairingResult(cachedProfileResult);
                }
            }

            if (!HasPreferredProfile)
            {
                return HostPairingActionResult.FromFailure("No saved Ansight pairing profile is available.");
            }

            return await ConnectUsingPreferredProfileCoreAsync(
                clientName,
                progress,
                cancellationToken,
                allowBundledRetry: true);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<HostPairingActionResult> ConnectUsingBundledProfileAsync(
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            return await ConnectUsingBundledProfileCoreAsync(clientName, progress, cancellationToken);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<HostPairingActionResult> ConnectFromPayloadAsync(
        string payload,
        string? sourceDescription = null,
        string? clientName = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return HostPairingActionResult.FromFailure("Paste or load a pairing config.");
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
            operationGate.Release();
        }
    }

    public HostPairingActionResult ClearStoredProfiles()
    {
        try
        {
            preferredProfileStore.Clear();
            var cachedProfileResult = hostConnection.ClearCachedProfile();
            if (!cachedProfileResult.Success)
            {
                return HostPairingActionResult.FromFailure(cachedProfileResult.Message);
            }

            return HostPairingActionResult.FromSuccess("Cleared stored Ansight pairing profiles.");
        }
        catch (Exception ex)
        {
            Logger.Exception(ex);
            return HostPairingActionResult.FromFailure($"Failed to clear stored Ansight pairing profiles: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        operationGate.Dispose();
    }

    private async Task<HostPairingActionResult> ConnectFromPayloadCoreAsync(
        string payload,
        string? sourceDescription,
        string? clientName,
        IProgress<string>? progress,
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
            return HostPairingActionResult.FromFailure(resolvedDocument.Message);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            resolvedDocument,
            clientName,
            progress,
            cancellationToken);
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

        return ToPairingResult(connectResult);
    }

    private async Task<HostPairingActionResult> ConnectUsingPreferredProfileCoreAsync(
        string? clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        bool allowBundledRetry)
    {
        var preferredDocument = await TryResolvePreferredPairingDocumentAsync();
        if (!preferredDocument.Success || preferredDocument.Document is null)
        {
            return HostPairingActionResult.FromFailure(preferredDocument.Message);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            preferredDocument,
            clientName,
            progress,
            cancellationToken);
        if (allowBundledRetry && ShouldRetryWithBundledProfile(connectResult, preferredDocument.Source))
        {
            return await ConnectUsingBundledProfileCoreAsync(clientName, progress, cancellationToken);
        }

        return ToPairingResult(connectResult);
    }

    private async Task<HostPairingActionResult> ConnectUsingBundledProfileCoreAsync(
        string? clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var bundledDocument = await TryResolveBundledPairingDocumentAsync(cancellationToken);
        if (!bundledDocument.Success || bundledDocument.Document is null)
        {
            return HostPairingActionResult.FromFailure(bundledDocument.Message);
        }

        var connectResult = await ConnectResolvedDocumentAsync(
            bundledDocument,
            clientName,
            progress,
            cancellationToken);
        return ToPairingResult(connectResult);
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
                baseDocument.Source);
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
                baseDocument.Source);
        }

        if (!hostConnection.TryParseAndValidateDocument(payload, out var document, out var error) || document is null)
        {
            return ResolvedPairingDocument.FromFailure(error);
        }

        return ResolvedPairingDocument.FromSuccess(
            document,
            $"Loaded {sourceDescription ?? "pairing code"}.",
            HostPairingDocumentSource.Payload);
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
            HostPairingDocumentSource.PreferredProfile));
    }

    private async Task<ResolvedPairingDocument> TryResolveBundledPairingDocumentAsync(CancellationToken cancellationToken)
    {
        var bundledDeveloperDocument = await TryLoadBundledDocumentAsync(
            options.BundledDeveloperProfileLoader,
            "Using bundled developer pairing config.",
            cancellationToken);
        if (bundledDeveloperDocument.Success)
        {
            return bundledDeveloperDocument;
        }

        var bundledDocument = await TryLoadBundledDocumentAsync(
            options.BundledProfileLoader,
            "Using bundled pairing config.",
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
            HostPairingDocumentSource.BundledProfile);
    }

    private async Task<HostConnectionActionResult> ConnectResolvedDocumentAsync(
        ResolvedPairingDocument resolvedDocument,
        string? clientName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
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
        if (!connectResult.Success && resolvedDocument.Source == HostPairingDocumentSource.PreferredProfile)
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

    private static HostPairingActionResult ToPairingResult(HostConnectionActionResult connectResult)
    {
        return connectResult.Success
            ? HostPairingActionResult.FromSuccess(connectResult.Message)
            : HostPairingActionResult.FromFailure(connectResult.Message);
    }

    private static bool ShouldRetryWithBundledProfile(
        HostConnectionActionResult connectResult,
        HostPairingDocumentSource source)
    {
        var rejectionCode = connectResult.SessionResult?.RejectionCode;
        return !connectResult.Success &&
               source == HostPairingDocumentSource.PreferredProfile &&
               !string.IsNullOrWhiteSpace(rejectionCode) &&
               StoredProfileResetReasonCodes.Contains(rejectionCode);
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

    private enum HostPairingDocumentSource
    {
        None = 0,
        PreferredProfile = 1,
        BundledProfile = 2,
        Payload = 3
    }

    private sealed record ResolvedPairingDocument(
        bool Success,
        ParsedPairingDocument? Document,
        string Message,
        HostPairingDocumentSource Source)
    {
        public static ResolvedPairingDocument FromFailure(string message)
            => new(false, null, message, HostPairingDocumentSource.None);

        public static ResolvedPairingDocument FromSuccess(
            ParsedPairingDocument document,
            string message,
            HostPairingDocumentSource source)
            => new(true, document, message, source);
    }
}
