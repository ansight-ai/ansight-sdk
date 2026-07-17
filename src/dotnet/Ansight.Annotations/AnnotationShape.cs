namespace Ansight.Annotations;

/// <summary>
/// Supported annotation mark shapes.
/// </summary>
public enum AnnotationShapeKind
{
    Rectangle,
    Ellipse,
    FreeDraw
}

/// <summary>
/// A normalized point in a free-draw annotation path.
/// </summary>
public readonly record struct AnnotationPoint
{
    public AnnotationPoint(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Annotation point coordinates must be finite.");
        }

        X = Math.Clamp(x, 0, 1);
        Y = Math.Clamp(y, 0, 1);
    }

    public double X { get; }

    public double Y { get; }
}

/// <summary>
/// A normalized mark over the captured screenshot. Coordinates are in the range zero to one.
/// </summary>
public sealed record AnnotationShape
{
    public AnnotationShape(AnnotationShapeKind kind, double x, double y, double width, double height)
    {
        if (kind == AnnotationShapeKind.FreeDraw)
        {
            throw new ArgumentException("Use the free-draw constructor when creating a free-draw annotation.", nameof(kind));
        }

        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Annotation shape coordinates must be finite.");
        }

        Kind = kind;
        X = Math.Clamp(x, 0, 1);
        Y = Math.Clamp(y, 0, 1);
        Width = Math.Clamp(width, 0, 1 - X);
        Height = Math.Clamp(height, 0, 1 - Y);
    }

    public AnnotationShape(IEnumerable<AnnotationPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var normalizedPoints = points.ToArray();
        if (normalizedPoints.Length < 2)
        {
            throw new ArgumentException("A free-draw annotation requires at least two points.", nameof(points));
        }

        Kind = AnnotationShapeKind.FreeDraw;
        Points = normalizedPoints;
        X = normalizedPoints.Min(point => point.X);
        Y = normalizedPoints.Min(point => point.Y);
        Width = normalizedPoints.Max(point => point.X) - X;
        Height = normalizedPoints.Max(point => point.Y) - Y;
    }

    public AnnotationShapeKind Kind { get; }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    /// <summary>
    /// Absolute normalized path points. Populated only for <see cref="AnnotationShapeKind.FreeDraw"/>.
    /// </summary>
    public IReadOnlyList<AnnotationPoint> Points { get; } = Array.Empty<AnnotationPoint>();

    /// <summary>
    /// Text associated specifically with this geometry.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    public string StrokeColor { get; init; } = "#FFFF3B30";

    public double StrokeWidth { get; init; } = 3;
}
