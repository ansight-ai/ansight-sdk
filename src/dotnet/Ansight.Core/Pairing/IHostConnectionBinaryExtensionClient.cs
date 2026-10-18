using System.Text.Json.Nodes;

namespace Ansight.Pairing;

internal interface IHostConnectionBinaryExtensionClient
{
    Task<OperationResult> PublishArtifactAsync(Ansight.Artifacts.ArtifactResult artifact, CancellationToken cancellationToken)
        => Task.FromResult(OperationResult.FromFailure("The active connection does not support artifact publication."));

    Task<OperationResult> SendBinaryExtensionAsync(
        string action,
        JsonObject payload,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}
