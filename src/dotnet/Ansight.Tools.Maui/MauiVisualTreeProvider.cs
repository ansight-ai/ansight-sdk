namespace Ansight.Tools.Maui;

using Ansight.Tools.VisualTree;

/// <summary>
/// Supplies the logical .NET MAUI visual tree to local capture and remote tools.
/// </summary>
public sealed class MauiVisualTreeProvider : IVisualTreeProvider
{
    /// <summary>
    /// Source identifier used for MAUI visual-tree capture.
    /// </summary>
    public const string SourceId = "maui";

    /// <summary>
    /// Shared stateless provider instance.
    /// </summary>
    public static MauiVisualTreeProvider Instance { get; } = new();

    private MauiVisualTreeProvider()
    {
    }

    public string Source => SourceId;

    public string DisplayName => ".NET MAUI";

    public Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
        => GetVisualTreeTool.CaptureAsync(arguments);

    public Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
        => new GetElementTool().Execute(arguments);
}
