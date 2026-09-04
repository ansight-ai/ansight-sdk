namespace Ansight.Annotations;

/// <summary>
/// Immutable, versioned annotation archive submitted to host or Offline Capture.
/// </summary>
public sealed class AnnotationBundle
{
    internal AnnotationBundle(Guid annotationId, DateTimeOffset capturedAtUtc, byte[] bytes)
    {
        AnnotationId = annotationId;
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public Guid AnnotationId { get; }

    /// <summary>
    /// Time at which annotation capture was requested, before evidence capture and editor presentation.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; }

    public string FileName => $"{CapturedAtUtc:yyyyMMddHHmmssfff}-{AnnotationId:N}.ansightannotation";

    public string MimeType => "application/vnd.ansight.annotation+zip";

    public ReadOnlyMemory<byte> Bytes { get; }
}
