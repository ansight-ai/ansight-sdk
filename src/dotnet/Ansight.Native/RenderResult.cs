using SkiaSharp;

namespace Ansight;

public readonly struct RenderResult
{
    public RenderResult(SKRect chartBounds, bool hasChartArea)
    {
        ChartBounds = chartBounds;
        HasChartArea = hasChartArea;
    }

    public SKRect ChartBounds { get; }

    public bool HasChartArea { get; }

    public static RenderResult Empty => new RenderResult(SKRect.Empty, false);
}
