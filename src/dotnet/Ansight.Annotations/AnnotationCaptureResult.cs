namespace Ansight.Annotations;

/// <summary>
/// Overall annotation capture outcome.
/// </summary>
public enum AnnotationCaptureStatus
{
    Completed,
    Queued,
    Cancelled,
    Disabled,
    Unavailable,
    Failed
}

/// <summary>
/// Result of capturing and delivering an annotation.
/// </summary>
public sealed class AnnotationCaptureResult
{
    internal AnnotationCaptureResult(
        AnnotationCaptureStatus status,
        Guid? annotationId = null,
        string? message = null,
        string? outboxPath = null,
        IReadOnlyList<AnnotationEvidenceResult>? evidence = null,
        IReadOnlyList<AnnotationSinkResult>? sinks = null)
    {
        Status = status;
        AnnotationId = annotationId;
        Message = message;
        OutboxPath = outboxPath;
        Evidence = evidence ?? Array.Empty<AnnotationEvidenceResult>();
        Sinks = sinks ?? Array.Empty<AnnotationSinkResult>();
    }

    public AnnotationCaptureStatus Status { get; }

    public Guid? AnnotationId { get; }

    public string? Message { get; }

    public string? OutboxPath { get; }

    public IReadOnlyList<AnnotationEvidenceResult> Evidence { get; }

    public IReadOnlyList<AnnotationSinkResult> Sinks { get; }

    public bool IsSuccess => Status is AnnotationCaptureStatus.Completed or AnnotationCaptureStatus.Queued;
}
