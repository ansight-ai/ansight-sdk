namespace Ansight.Tools.VisualTree;

/// <summary>
/// Captures the built-in Android or Apple native view hierarchy.
/// </summary>
public sealed class NativeVisualTreeProvider : IVisualTreeProvider
{
    /// <summary>
    /// Shared stateless provider instance.
    /// </summary>
    public static NativeVisualTreeProvider Instance { get; } = new();

    private NativeVisualTreeProvider()
    {
    }

    public string Source => VisualTreeProviderRegistry.NativeSource;

    public string DisplayName => "Native";

    public Task<ToolResult> GetVisualTreeAsync(IReadOnlyDictionary<string, string> arguments)
        => VisualTreeSupport.GetNativeVisualTreeAsync(arguments);

    public Task<ToolResult> InspectNodeAsync(IReadOnlyDictionary<string, string> arguments)
        => VisualTreeSupport.InspectNativeNodeAsync(arguments);
}
