using Ansight.Tools;

namespace Ansight.Native;

internal sealed class TransferOfflineCapturesTool : ITool
{
    public string Id => "offline_capture.transfer";
    public string Name => "Transfer offline captures";
    public string Description => "Import retained offline sessions into this host, then purge acknowledged local copies.";
    public string Category => "Diagnostics";
    public ToolPolicy Policy => ToolPolicy.Write;
    public string Keywords => "offline captures import";
    public ToolSchema ArgumentsSchema => ToolSchema.Object();
    public ToolSchema ResultSchema => ToolSchema.Object();
    public async Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        var result = await Runtime.TransferOfflineCapturesAsync().ConfigureAwait(false);
        return result.Success ? ToolResult.Success(message: result.Message) : ToolResult.Failure(result.Message);
    }
}
