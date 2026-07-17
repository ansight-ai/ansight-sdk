namespace Ansight.Annotations;

/// <summary>
/// Kinds of evidence that can be attached to an annotation.
/// </summary>
public enum AnnotationEvidenceKind
{
    Screenshot,
    VisualTree,
    Artifact
}

/// <summary>
/// Outcome of attempting to capture one evidence source.
/// </summary>
public enum AnnotationEvidenceStatus
{
    Captured,
    Unavailable,
    NotPermitted,
    TimedOut,
    Failed,
    Skipped
}

/// <summary>
/// Describes evidence before capture so the host app can apply policy.
/// </summary>
/// <param name="Id">Stable evidence identifier.</param>
/// <param name="Kind">Evidence kind.</param>
/// <param name="DisplayName">Human-readable evidence name.</param>
public sealed record AnnotationEvidenceDescriptor(
    string Id,
    AnnotationEvidenceKind Kind,
    string DisplayName);

/// <summary>
/// Host policy decision for one evidence source.
/// </summary>
/// <param name="IsPermitted">Whether capture is permitted.</param>
/// <param name="Reason">Optional explanation when capture is denied.</param>
public sealed record AnnotationEvidenceDecision(bool IsPermitted, string? Reason = null)
{
    public static AnnotationEvidenceDecision Permit { get; } = new(true);

    public static AnnotationEvidenceDecision Deny(string? reason = null) => new(false, reason);
}

/// <summary>
/// Allows the host app to permit or deny individual annotation evidence sources.
/// </summary>
public interface IAnnotationEvidencePolicy
{
    AnnotationEvidenceDecision Evaluate(AnnotationEvidenceDescriptor evidence);
}

/// <summary>
/// Captured evidence outcome returned with an annotation result.
/// </summary>
public sealed record AnnotationEvidenceResult(
    string Id,
    AnnotationEvidenceKind Kind,
    AnnotationEvidenceStatus Status,
    string? Reason = null,
    DateTimeOffset? CapturedAtUtc = null,
    long? SizeBytes = null,
    bool Truncated = false);

internal sealed class PermitAllAnnotationEvidencePolicy : IAnnotationEvidencePolicy
{
    internal static PermitAllAnnotationEvidencePolicy Instance { get; } = new();

    public AnnotationEvidenceDecision Evaluate(AnnotationEvidenceDescriptor evidence)
        => AnnotationEvidenceDecision.Permit;
}
