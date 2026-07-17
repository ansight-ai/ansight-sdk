using System.Text.Json.Nodes;

namespace Ansight.Pairing;

internal interface IHostConnectionBinaryExtensionClient
{
    Task<OperationResult> SendBinaryExtensionAsync(
        string action,
        JsonObject payload,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}
