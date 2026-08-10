using System.Text.Json.Nodes;

namespace Ansight.Screenshot;

/// <summary>
/// Connects optional visual-tree providers to screenshot-and-tree session capture.
/// </summary>
public static class SessionVisualTreeCaptureRegistry
{
    private static readonly Lock gate = new();
    private static Func<CancellationToken, Task<IReadOnlyList<JsonObject>>>? provider;

    /// <summary>
    /// Sets the process-wide provider used to capture visual trees alongside screenshots.
    /// </summary>
    public static void SetProvider(Func<CancellationToken, Task<IReadOnlyList<JsonObject>>>? provider)
    {
        lock (gate)
        {
            SessionVisualTreeCaptureRegistry.provider = provider;
        }
    }

    internal static Task<IReadOnlyList<JsonObject>> CaptureAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<IReadOnlyList<JsonObject>>>? provider;
        lock (gate)
        {
            provider = SessionVisualTreeCaptureRegistry.provider;
        }

        return provider?.Invoke(cancellationToken)
               ?? Task.FromResult<IReadOnlyList<JsonObject>>(Array.Empty<JsonObject>());
    }
}
