using Ansight.Pairing;
using Ansight.Pairing.Models;

namespace Ansight.Native;

internal sealed class NativeHostConnection : IHostConnection, IDisposable
{
    private static readonly TimeSpan StatusPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly INativeRuntimeBridge nativeRuntime;
    private readonly HostConnectionOptions options;
    private readonly PairingConfigDocumentService documentService = new();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly Lock statusGate = new();
    private readonly Timer statusTimer;
    private HostConnectionStatus status;
    private HostConnectionCapabilities capabilities;
    private bool disposed;

    internal NativeHostConnection(INativeRuntimeBridge nativeRuntime, HostConnectionOptions options)
    {
        this.nativeRuntime = nativeRuntime ?? throw new ArgumentNullException(nameof(nativeRuntime));
        this.options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));
        status = nativeRuntime.HostConnectionStatus;
        capabilities = MergeManagedReaderCapabilities(nativeRuntime.HostConnectionCapabilities);
        statusTimer = new Timer(
            static state => ((NativeHostConnection)state!).RefreshSnapshot(),
            this,
            StatusPollInterval,
            StatusPollInterval);
    }

    public bool HasSavedConfig => Status.HasSavedConfig;

    public bool IsConnected => Status.IsConnected;

    public HostConnectionStatus Status
    {
        get
        {
            RefreshSnapshot();
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
            RefreshSnapshot();
            lock (statusGate)
            {
                return capabilities;
            }
        }
    }

    public event EventHandler<HostConnectionChangedEventArgs>? StatusChanged;

    public Task<HostConnectionCapabilities> RefreshCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshSnapshot(forceChangedEvent: true);
        return Task.FromResult(Capabilities);
    }

    public Task<HostConnectionResult> NotifyConfigChangedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = nativeRuntime.NotifyHostConnectionConfigChanged();
        RefreshSnapshot(forceChangedEvent: true);
        return Task.FromResult(result);
    }

    public bool TryParseConfigDocument(
        string payload,
        out PairingConfigDocument? config,
        out string error)
        => documentService.TryParseConfigDocument(payload, out config, out error);

    public async Task<HostConnectionResult> ConnectAsync(
        HostConnectionRequest? request = null,
        string? clientName = null,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        request ??= HostConnectionRequest.Auto();
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var nativeRequest = await ResolveRequestAsync(request, clientName, cancellationToken);
            if (nativeRequest is null)
            {
                return HostConnectionResult.FromFailure(
                    $"No host connection reader is available for '{request.Kind}'.",
                    HostConnectionActionKind.Connect,
                    HostConnectionSource.ConfigReader);
            }

            var result = await nativeRuntime.ConnectAsync(nativeRequest, cancellationToken);
            RefreshSnapshot(forceChangedEvent: true);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Exception(exception);
            RefreshSnapshot(forceChangedEvent: true);
            return HostConnectionResult.FromFailure(
                $"The native Ansight runtime could not connect: {exception.Message}",
                HostConnectionActionKind.Connect);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task<OperationResult> SendClientLogAsync(
        string logLine,
        IProgress<HostConnectionProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
        => nativeRuntime.SendClientLogAsync(logLine, cancellationToken);

    public async Task<HostConnectionResult> DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var result = await nativeRuntime.DisconnectAsync(cancellationToken);
            RefreshSnapshot(forceChangedEvent: true);
            return result;
        }
        finally
        {
            operationGate.Release();
        }
    }

    public HostConnectionResult ClearSavedConfigs()
    {
        var savedResult = nativeRuntime.ClearSavedPairing();
        var cachedResult = nativeRuntime.ClearCachedSession();
        RefreshSnapshot(forceChangedEvent: true);

        if (!savedResult.Success)
        {
            return savedResult;
        }
        if (!cachedResult.Success)
        {
            return HostConnectionResult.FromFailure(
                cachedResult.Message,
                HostConnectionActionKind.ClearSavedConfigs,
                HostConnectionSource.CachedSession);
        }

        return HostConnectionResult.FromSuccess(
            "Saved host registration and cached session cleared.",
            HostConnectionActionKind.ClearSavedConfigs,
            HostConnectionSource.SavedConfig);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        statusTimer.Dispose();
        operationGate.Dispose();
    }

    private async Task<NativeHostConnectionRequest?> ResolveRequestAsync(
        HostConnectionRequest request,
        string? clientName,
        CancellationToken cancellationToken)
    {
        var kind = request.Kind;
        var payload = request.Payload;

        if (kind == HostConnectionRequestKind.Config)
        {
            ArgumentNullException.ThrowIfNull(request.Config);
            payload = PairingConfigDocumentJson.Serialize(request.Config);
        }
        else if (kind is HostConnectionRequestKind.File or HostConnectionRequestKind.QrCode)
        {
            var reader = options.ConfigReader;
            if (reader is null || !reader.CanRead(kind))
            {
                return null;
            }

            payload = await reader.ReadConfigPayloadAsync(request, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            kind = HostConnectionRequestKind.Payload;
        }

        return new NativeHostConnectionRequest(
            kind,
            payload,
            clientName);
    }

    private void RefreshSnapshot(bool forceChangedEvent = false)
    {
        if (disposed)
        {
            return;
        }

        try
        {
            var nextStatus = nativeRuntime.HostConnectionStatus;
            var nextCapabilities = MergeManagedReaderCapabilities(
                nativeRuntime.HostConnectionCapabilities);
            HostConnectionChangedEventArgs? eventArgs = null;

            lock (statusGate)
            {
                if (forceChangedEvent || nextStatus != status || nextCapabilities != capabilities)
                {
                    status = nextStatus;
                    capabilities = nextCapabilities;
                    eventArgs = new HostConnectionChangedEventArgs(status, capabilities);
                }
            }

            if (eventArgs is not null)
            {
                StatusChanged?.Invoke(this, eventArgs);
            }
        }
        catch (Exception exception)
        {
            Logger.Warning($"The native host connection status could not be refreshed: {exception.Message}");
        }
    }

    private HostConnectionCapabilities MergeManagedReaderCapabilities(
        HostConnectionCapabilities nativeCapabilities)
    {
        var reader = options.ConfigReader;
        return nativeCapabilities with
        {
            CanChooseConfigFile =
                nativeCapabilities.CanChooseConfigFile ||
                CanRead(reader, HostConnectionRequestKind.File),
            CanScanConfigQrCode =
                nativeCapabilities.CanScanConfigQrCode ||
                CanRead(reader, HostConnectionRequestKind.QrCode)
        };
    }

    private static bool CanRead(
        IHostConnectionConfigReader? reader,
        HostConnectionRequestKind kind)
    {
        if (reader is null)
        {
            return false;
        }

        try
        {
            return reader.CanRead(kind);
        }
        catch
        {
            return false;
        }
    }
}
