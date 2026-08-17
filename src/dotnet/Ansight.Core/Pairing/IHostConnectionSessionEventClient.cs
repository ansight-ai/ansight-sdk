using System.Text.Json.Nodes;

namespace Ansight.Pairing;

internal interface IHostConnectionSessionEventClient
{
    Task<OperationResult> SendSessionEventAsync(
        string type,
        JsonObject payload,
        CancellationToken cancellationToken);
}
