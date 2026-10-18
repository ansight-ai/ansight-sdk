namespace Ansight.Artifacts;

using System.Text.Json.Nodes;
using Ansight.Pairing;

internal static class ArtifactPublication
{
    internal static async Task<OperationResult> SendAsync(
        ArtifactResult artifact,
        Func<bool> isConnected,
        Func<string, JsonObject, CancellationToken, Task<OperationResult>> sendControl,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task<OperationResult>> sendBinary,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        var transferId = Guid.NewGuid();
        var identity = new JsonObject { ["transferId"] = transferId.ToString("N") };
        var begun = false;
        var committed = false;
        try
        {
            ValidateMetadata(artifact.Metadata);
            ArgumentNullException.ThrowIfNull(artifact.Payload);
            if (!isConnected()) return OperationResult.FromFailure("A live Ansight host session is not connected.");

            var sizeBytes = artifact.Payload.SizeBytes ?? artifact.Metadata.SizeBytes;
            if (sizeBytes is < 0 or > ArtifactPublishProtocol.MaximumSizeBytes)
                return OperationResult.FromFailure("Published artifacts must be at most 512 MiB.");
            if (artifact.Metadata.SizeBytes is { } declaredSize && sizeBytes != declaredSize)
                return OperationResult.FromFailure("Artifact metadata size does not match its payload size.");

            await using var stream = await artifact.Payload.OpenReadAsync(cancellationToken);
            if (stream is null || !stream.CanRead) return OperationResult.FromFailure("Artifact payload must be readable.");
            var payload = new JsonObject
            {
                ["schema"] = ArtifactPublishProtocol.Schema,
                ["transferId"] = transferId.ToString("N"),
                ["wireProtocol"] = PairingFileTransferWireProtocol.ProtocolName,
                ["artifact"] = ArtifactToolJson.ToJson(artifact.Metadata with { SizeBytes = sizeBytes })
            };
            var accepted = await sendControl(ArtifactPublishProtocol.BeginAction, payload, cancellationToken);
            if (!accepted.Success) return accepted;
            begun = true;

            var buffer = new byte[ArtifactPublishProtocol.ChunkBytes];
            var sequence = 0;
            long offset = 0;
            while (true)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0) break;
                if (offset + count > ArtifactPublishProtocol.MaximumSizeBytes || sizeBytes is { } expected && offset + count > expected)
                    return OperationResult.FromFailure("Artifact payload exceeded its declared size or the 512 MiB limit.");
                var sent = await sendBinary(PairingFileTransferWireProtocol.CreateFrame(
                    transferId, PairingFileTransferFrameType.Chunk, sequence++, offset, buffer.AsSpan(0, count)), cancellationToken);
                if (!sent.Success) return sent;
                offset += count;
            }
            if (sizeBytes is { } length && offset != length)
                return OperationResult.FromFailure("Artifact payload ended before its declared size.");

            cancellationToken.ThrowIfCancellationRequested();
            var complete = await sendBinary(PairingFileTransferWireProtocol.CreateFrame(
                transferId, PairingFileTransferFrameType.Complete, sequence, offset, ReadOnlySpan<byte>.Empty), cancellationToken);
            if (!complete.Success) return complete;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await sendControl(ArtifactPublishProtocol.CommitAction, identity, cancellationToken);
            committed = result.Success;
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            return OperationResult.FromFailure($"Could not publish artifact: {exception.Message}");
        }
        finally
        {
            if (begun && !committed)
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await sendControl(ArtifactPublishProtocol.AbortAction, identity, cleanup.Token);
                }
                catch (Exception exception) { Logger.Warning($"Artifact transfer cleanup failed: {exception.Message}"); }
            }
        }
    }

    private static void ValidateMetadata(ArtifactMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.MimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.FileName);
    }
}
