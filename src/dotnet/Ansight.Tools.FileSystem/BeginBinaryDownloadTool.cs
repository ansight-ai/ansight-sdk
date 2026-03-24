namespace Ansight.Tools.FileSystem;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Ansight.Pairing;

public sealed class BeginBinaryDownloadTool : ITool
{
    private const int DefaultChunkBytes = 64 * 1024;
    private const int AbsoluteMaxChunkBytes = 512 * 1024;
    private readonly FileSystemToolsOptions options;
    private readonly Func<PairingBinaryTransferHub?> transferHubFactory;

    public BeginBinaryDownloadTool(FileSystemToolsOptions? options = null)
        : this(
            options,
            static () => Runtime.IsInitialized
                ? Runtime.MutableInstance.BinaryTransferHub
                : null)
    {
    }

    internal BeginBinaryDownloadTool(
        FileSystemToolsOptions? options,
        Func<PairingBinaryTransferHub?> transferHubFactory)
    {
        this.options = options ?? FileSystemToolsOptions.Default;
        this.transferHubFactory = transferHubFactory ?? throw new ArgumentNullException(nameof(transferHubFactory));
    }

    public string Category => "files";

    public ToolScope Scope => ToolScope.Read;

    public string Id => FileSystemToolIds.BeginBinaryDownload;

    public string Name => "Begin Binary Download";

    public string Description => "Starts a binary WebSocket download for a sandboxed file so the host can materialize it locally.";

    public string Keywords => "filesystem file download binary websocket sandbox transfer";

    public ToolSchema ArgumentsSchema => FileSystemToolSchemas.BeginBinaryDownloadArguments;

    public ToolSchema ResultSchema => FileSystemToolSchemas.BeginBinaryDownloadResult;

    public ToolSecurity Security => FileSystemToolSecurityProfiles.BeginBinaryDownload;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        FileStream? stream = null;
        try
        {
            var requestId = FileSystemSandbox.GetString(arguments, ToolExecutionArgumentNames.RequestId);
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return Task.FromResult(ToolResult.Failure(
                    "Binary downloads require a live tool protocol request context.",
                    errorCode: "filesystem_binary_download_unavailable"));
            }

            var transferHub = transferHubFactory();
            if (transferHub is null)
            {
                return Task.FromResult(ToolResult.Failure(
                    "Binary downloads require an active initialized runtime and pairing session.",
                    errorCode: "filesystem_binary_download_unavailable"));
            }

            var roots = FileSystemSandbox.GetRoots(options);
            var resolvedFile = FileSystemSandbox.ResolvePath(arguments, roots, requireExisting: true, expectDirectory: false);
            var chunkBytes = FileSystemSandbox.GetInt(arguments, "chunkBytes", defaultValue: DefaultChunkBytes, minimum: 1024, maximum: AbsoluteMaxChunkBytes);
            var fileInfo = new FileInfo(resolvedFile.FullPath);
            var transferId = Guid.NewGuid();
            var downloadId = FileSystemSandbox.GetString(arguments, "downloadId") ?? requestId;

            stream = new FileStream(
                resolvedFile.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: chunkBytes,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            var transferStream = stream;

            var pendingTransfer = new PairingBinaryTransferHub.PendingBinaryTransfer(
                description: $"{Id}:{transferId:N}",
                startAsync: (transport, cancellationToken) => StreamFileAsync(
                    transport,
                    transferId,
                    transferStream,
                    chunkBytes,
                    cancellationToken),
                abandon: () => transferStream.Dispose());

            if (!transferHub.TryQueueTransfer(requestId, pendingTransfer, out var error))
            {
                stream.Dispose();
                return Task.FromResult(ToolResult.Failure(
                    error,
                    errorCode: "filesystem_binary_download_unavailable"));
            }

            stream = null;

            var payload = FileSystemContentDescriptor.CreateResolvedFilePayload(resolvedFile, roots, fileInfo);
            payload["downloadId"] = downloadId;
            payload["transferId"] = transferId.ToString("N");
            payload["deliveryMode"] = "websocket_binary";
            payload["wireProtocol"] = PairingFileTransferWireProtocol.ProtocolName;
            payload["status"] = "queued";
            payload["chunkBytes"] = chunkBytes;
            payload["capturedAtUtc"] = DateTime.UtcNow.ToString("O");

            return Task.FromResult(ToolResult.Success(payload));
        }
        catch (Exception exception)
        {
            stream?.Dispose();
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "filesystem_binary_download_failed"));
        }
    }

    private static async Task StreamFileAsync(
        PairingSessionTransport transport,
        Guid transferId,
        FileStream stream,
        int chunkBytes,
        CancellationToken cancellationToken)
    {
        Logger.Info($"Binary file transfer {transferId:N} started with chunk size {chunkBytes} bytes.");
        var buffer = new byte[chunkBytes];
        var sequence = 0;
        var offsetBytes = 0L;

        try
        {
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    break;
                }

                var chunkFrame = PairingFileTransferWireProtocol.CreateFrame(
                    transferId,
                    PairingFileTransferFrameType.Chunk,
                    sequence,
                    offsetBytes,
                    buffer.AsSpan(0, bytesRead));
                await SendFrameAsync(transport, chunkFrame, cancellationToken);

                sequence++;
                offsetBytes += bytesRead;
            }

            var completeFrame = PairingFileTransferWireProtocol.CreateFrame(
                transferId,
                PairingFileTransferFrameType.Complete,
                sequence,
                offsetBytes,
                ReadOnlySpan<byte>.Empty);
            await SendFrameAsync(transport, completeFrame, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning($"Binary file transfer {transferId:N} failed: {exception.Message}");
            await TrySendErrorFrameAsync(transport, transferId, sequence, offsetBytes, exception, cancellationToken);
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    private static async Task SendFrameAsync(
        PairingSessionTransport transport,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        var result = await transport.SendBinaryAsync(frame, WebSocketMessageType.Binary, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    private static async Task TrySendErrorFrameAsync(
        PairingSessionTransport transport,
        Guid transferId,
        int sequence,
        long offsetBytes,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = Encoding.UTF8.GetBytes(exception.Message);
            var errorFrame = PairingFileTransferWireProtocol.CreateFrame(
                transferId,
                PairingFileTransferFrameType.Error,
                sequence,
                offsetBytes,
                payload);
            _ = await transport.SendBinaryAsync(errorFrame, WebSocketMessageType.Binary, cancellationToken);
        }
        catch
        {
        }
    }
}
