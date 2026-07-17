namespace Ansight.Annotations;

/// <summary>
/// Receives sealed annotation bundles for live or offline delivery.
/// </summary>
public interface IAnnotationSink
{
    string Id { get; }

    ValueTask<AnnotationSinkResult> SubmitAsync(AnnotationBundle bundle, CancellationToken cancellationToken);
}

/// <summary>
/// Result returned by an annotation delivery sink.
/// </summary>
public sealed record AnnotationSinkResult(string SinkId, bool IsSuccess, string? Message = null)
{
    public static AnnotationSinkResult Success(string sinkId, string? message = null) => new(sinkId, true, message);

    public static AnnotationSinkResult Failure(string sinkId, string? message = null) => new(sinkId, false, message);
}
