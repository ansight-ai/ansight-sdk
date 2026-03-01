namespace Ansight;

/// <summary>
/// UI-facing projection of a <see cref="AppEvent"/> for binding.
/// </summary>
public class AppEventDisplay
{
    public string Symbol { get; init; } = "";
    public string Label { get; init; } = "";
    public string Details { get; init; } = "";
    public bool HasDetails { get; init; }
    public Color ChannelColor { get; init; } = Colors.WhiteSmoke;
    public string Timestamp { get; init; } = "";
}
