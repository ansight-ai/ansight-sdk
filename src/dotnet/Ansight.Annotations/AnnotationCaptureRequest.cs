namespace Ansight.Annotations;

using System.Text.Json.Nodes;

/// <summary>
/// User feedback and marks to submit with an annotation capture.
/// </summary>
public sealed class AnnotationCaptureRequest
{
    /// <summary>
    /// Overall text for the complete annotation capture. Individual geometry text is stored on each shape.
    /// </summary>
    public string? Feedback { get; init; }

    public IReadOnlyList<AnnotationShape> Shapes { get; init; } = Array.Empty<AnnotationShape>();

    public IReadOnlyDictionary<string, JsonNode?> CustomData { get; init; } =
        new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
}
