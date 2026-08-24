namespace Ansight.Pairing;

internal sealed class PairingBinaryTransferHub
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, List<PendingBinaryTransfer>> pendingTransfers = new(StringComparer.Ordinal);
    private IPairingBinaryTransport? transport;

    internal void AttachTransport(IPairingBinaryTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        lock (gate)
        {
            ClearPendingTransfersUnsafe();
            this.transport = transport;
        }
    }

    internal void DetachTransport(IPairingBinaryTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);

        lock (gate)
        {
            if (!ReferenceEquals(this.transport, transport))
            {
                return;
            }

            this.transport = null;
            ClearPendingTransfersUnsafe();
        }
    }

    internal bool TryQueueTransfer(string requestId, PendingBinaryTransfer transfer, out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentNullException.ThrowIfNull(transfer);

        lock (gate)
        {
            if (transport is null || !transport.IsOpen)
            {
                error = "Binary downloads require an active pairing WebSocket session.";
                return false;
            }

            if (!pendingTransfers.TryGetValue(requestId, out var requestTransfers))
            {
                requestTransfers = [];
                pendingTransfers[requestId] = requestTransfers;
            }

            requestTransfers.Add(transfer);
        }

        Logger.Info($"Queued binary transfer '{transfer.Description}' for request '{requestId}'.");
        error = string.Empty;
        return true;
    }

    internal bool TryStartQueuedTransfer(string requestId)
    {
        List<PendingBinaryTransfer>? transfers;
        IPairingBinaryTransport? transport;

        lock (gate)
        {
            if (!pendingTransfers.Remove(requestId, out transfers))
            {
                return false;
            }

            transport = this.transport;
        }

        if (transfers is null || transfers.Count == 0)
        {
            return false;
        }

        if (transport is null || !transport.IsOpen)
        {
            foreach (var transfer in transfers)
            {
                transfer.Abandon();
            }
            return false;
        }

        Logger.Info($"Starting {transfers.Count} binary transfer(s) for request '{requestId}'.");
        _ = Task.Run(async () =>
        {
            foreach (var transfer in transfers)
            {
                try
                {
                    await transfer.StartAsync(transport, CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    Logger.Warning($"Binary transfer '{transfer.Description}' failed: {exception.Message}");
                }
            }
        });

        return true;
    }

    private void ClearPendingTransfersUnsafe()
    {
        foreach (var requestTransfers in pendingTransfers.Values)
        {
            foreach (var pendingTransfer in requestTransfers)
            {
                pendingTransfer.Abandon();
            }
        }

        pendingTransfers.Clear();
    }

    internal sealed class PendingBinaryTransfer
    {
        private readonly Action? abandon;

        internal PendingBinaryTransfer(
            string description,
            Func<IPairingBinaryTransport, CancellationToken, Task> startAsync,
            Action? abandon = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(description);
            ArgumentNullException.ThrowIfNull(startAsync);

            Description = description;
            StartAsync = startAsync;
            this.abandon = abandon;
        }

        internal string Description { get; }

        internal Func<IPairingBinaryTransport, CancellationToken, Task> StartAsync { get; }

        internal void Abandon()
        {
            try
            {
                abandon?.Invoke();
            }
            catch (Exception exception)
            {
                Logger.Warning($"Binary transfer '{Description}' cleanup failed: {exception.Message}");
            }
        }
    }
}
