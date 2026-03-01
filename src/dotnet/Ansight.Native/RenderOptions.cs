namespace Ansight;

/// <summary>
/// Options used by <see cref="ChartRenderer"/> when drawing a chart frame.
/// </summary>
public struct RenderOptions
{
    public required IReadOnlyList<byte> Channels { get; init; }
    
    public required DateTime FromUtc { get; init; }
    
    public required DateTime ToUtc { get; init; }
    
    public required DateTime? CurrentUtc { get; init; }
    
    public required ChartRenderMode Mode { get; init; }

    public required float? ProbePosition { get; init; }
    
    public required AppEventRenderingBehaviour AppEventRenderingBehaviour { get; init; }
    
    public required ChartTheme Theme { get; init; }
}
