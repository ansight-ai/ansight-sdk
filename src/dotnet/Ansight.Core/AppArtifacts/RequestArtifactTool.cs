namespace Ansight.Artifacts;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Ansight.Pairing;
using Ansight.Tools;

/// <summary>
/// Remote tool that requests and streams an app-provided artifact snapshot.
/// </summary>
public sealed class RequestArtifactTool : ITool
{
    private const int DefaultChunkBytes = 64 * 1024;
    private const int AbsoluteMaxChunkBytes = 512 * 1024;
    private readonly Func<ArtifactRegistry> providersFactory;
    private readonly Func<PairingBinaryTransferHub?> transferHubFactory;

    /// <summary>
    /// Creates a request tool over the supplied artifact providers.
    /// </summary>
    public RequestArtifactTool(ArtifactRegistry providers)
        : this(
            () => providers,
            static () => Runtime.IsInitialized
                ? Runtime.MutableInstance.BinaryTransferHub
                : null)
    {
        ArgumentNullException.ThrowIfNull(providers);
    }

    internal RequestArtifactTool(
        ArtifactRegistry providers,
        Func<PairingBinaryTransferHub?> transferHubFactory)
        : this(() => providers, transferHubFactory)
    {
        ArgumentNullException.ThrowIfNull(providers);
    }

    internal RequestArtifactTool(
        Func<ArtifactRegistry> providersFactory,
        Func<PairingBinaryTransferHub?> transferHubFactory)
    {
        this.providersFactory = providersFactory ?? throw new ArgumentNullException(nameof(providersFactory));
        this.transferHubFactory = transferHubFactory ?? throw new ArgumentNullException(nameof(transferHubFactory));
    }

    public string Category => "artifacts";

    public ToolPolicy Policy => ToolPolicy.Read;

    public string Id => ArtifactToolIds.Request;

    public string Name => "Request Artifact";

    public string Description => "Requests an app-provided artifact snapshot and streams it to the host.";

    public string Keywords => "artifact artifacts request export snapshot binary stream";

    public ToolSchema ArgumentsSchema => ArtifactToolSchemas.RequestArguments;

    public ToolSchema ResultSchema => ArtifactToolSchemas.RequestResult;

    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Stream? stream = null;
        try
        {
            var requestId = ArtifactToolArgumentReader.GetString(arguments, ToolExecutionArgumentNames.RequestId);
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return ToolResult.Failure(
                    "Artifact requests require a live tool protocol request context.",
                    errorCode: "artifact_request_unavailable");
            }

            var providerId = ArtifactToolArgumentReader.GetString(arguments, "providerId");
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return ToolResult.Failure(
                    "Artifact request must include 'providerId'.",
                    errorCode: "artifact_request_missing_provider_id");
            }

            var artifactId = ArtifactToolArgumentReader.GetString(arguments, "artifactId");
            if (string.IsNullOrWhiteSpace(artifactId))
            {
                return ToolResult.Failure(
                    "Artifact request must include 'artifactId'.",
                    errorCode: "artifact_request_missing_artifact_id");
            }

            var providers = providersFactory() ?? ArtifactRegistry.Empty;
            if (!providers.TryGet(providerId, out var provider) || provider == null)
            {
                return ToolResult.Failure(
                    $"Artifact provider '{providerId}' is not registered.",
                    errorCode: "artifact_provider_not_found");
            }

            var transferHub = transferHubFactory();
            if (transferHub is null)
            {
                return ToolResult.Failure(
                    "Artifact requests require an active initialized runtime and pairing session.",
                    errorCode: "artifact_transfer_unavailable");
            }

            var requestedAtUtc = DateTimeOffset.UtcNow;
            var sessionId = ArtifactToolArgumentReader.GetString(arguments, ToolExecutionArgumentNames.SessionId);
            var requestArguments = ArtifactToolArgumentReader.GetNestedStringArguments(arguments);
            var artifactRequest = new ArtifactRequest(
                providerId,
                artifactId,
                requestArguments,
                new ArtifactRequestContext(requestId, sessionId, requestedAtUtc));
            var artifactResult = await provider.CreateAsync(artifactRequest, CancellationToken.None);
            if (artifactResult == null)
            {
                return ToolResult.Failure(
                    $"Artifact provider '{providerId}' returned no artifact result.",
                    errorCode: "artifact_request_failed");
            }

            ValidateResult(providerId, artifactId, artifactResult);

            var chunkBytes = ArtifactToolArgumentReader.GetInt(
                arguments,
                "chunkBytes",
                defaultValue: DefaultChunkBytes,
                minimum: 1024,
                maximum: AbsoluteMaxChunkBytes);
            var transferId = Guid.NewGuid();
            var downloadId = ArtifactToolArgumentReader.GetString(arguments, "downloadId") ?? requestId;
            stream = await artifactResult.Payload.OpenReadAsync(CancellationToken.None);
            var transferStream = stream;

            var pendingTransfer = new PairingBinaryTransferHub.PendingBinaryTransfer(
                description: $"{Id}:{providerId}:{artifactId}:{transferId:N}",
                startAsync: (transport, cancellationToken) => StreamArtifactAsync(
                    transport,
                    transferId,
                    transferStream,
                    chunkBytes,
                    cancellationToken),
                abandon: () => transferStream.Dispose());

            if (!transferHub.TryQueueTransfer(requestId, pendingTransfer, out var error))
            {
                stream.Dispose();
                return ToolResult.Failure(
                    error,
                    errorCode: "artifact_transfer_unavailable");
            }

            stream = null;
            var metadata = artifactResult.Metadata.SizeBytes is null && artifactResult.Payload.SizeBytes is not null
                ? artifactResult.Metadata with { SizeBytes = artifactResult.Payload.SizeBytes }
                : artifactResult.Metadata;

            var payload = new JsonObject
            {
                ["artifact"] = ArtifactToolJson.ToJson(metadata),
                ["downloadId"] = downloadId,
                ["transferId"] = transferId.ToString("N"),
                ["deliveryMode"] = "websocket_binary",
                ["wireProtocol"] = PairingFileTransferWireProtocol.ProtocolName,
                ["status"] = "queued",
                ["chunkBytes"] = chunkBytes,
                ["capturedAtUtc"] = requestedAtUtc.ToString("O")
            };

            return ToolResult.Success(payload);
        }
        catch (Exception exception)
        {
            stream?.Dispose();
            return ToolResult.Failure(exception.Message, errorCode: "artifact_request_failed");
        }
    }

    private static void ValidateResult(string providerId, string artifactId, ArtifactResult artifactResult)
    {
        ArgumentNullException.ThrowIfNull(artifactResult.Metadata);
        ArgumentNullException.ThrowIfNull(artifactResult.Payload);

        if (!string.Equals(artifactResult.Metadata.ProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Artifact metadata provider id must match the requested provider id.");
        }

        if (!string.Equals(artifactResult.Metadata.ArtifactId, artifactId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Artifact metadata artifact id must match the requested artifact id.");
        }

        if (string.IsNullOrWhiteSpace(artifactResult.Metadata.Name))
        {
            throw new InvalidOperationException("Artifact metadata name must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(artifactResult.Metadata.Kind))
        {
            throw new InvalidOperationException("Artifact metadata kind must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(artifactResult.Metadata.MimeType))
        {
            throw new InvalidOperationException("Artifact metadata MIME type must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(artifactResult.Metadata.FileName))
        {
            throw new InvalidOperationException("Artifact metadata file name must be non-empty.");
        }
    }

    private static async Task StreamArtifactAsync(
        IPairingBinaryTransport transport,
        Guid transferId,
        Stream stream,
        int chunkBytes,
        CancellationToken cancellationToken)
    {
        Logger.Info($"Artifact transfer {transferId:N} started with chunk size {chunkBytes} bytes.");
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning($"Artifact transfer {transferId:N} failed: {exception.Message}");
            await TrySendErrorFrameAsync(transport, transferId, sequence, offsetBytes, exception, cancellationToken);
        }
        finally
        {
            await stream.DisposeAsync();
        }
    }

    private static async Task SendFrameAsync(
        IPairingBinaryTransport transport,
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
        IPairingBinaryTransport transport,
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
