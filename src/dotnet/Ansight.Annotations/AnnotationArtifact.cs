namespace Ansight.Annotations;

using Ansight.Artifacts;

/// <summary>
/// Host-provided artifact to materialize inside an annotation bundle.
/// </summary>
public sealed class AnnotationArtifact
{
    public AnnotationArtifact(string name, string mimeType, string fileName, IArtifactPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Name = name.Trim();
        MimeType = mimeType.Trim();
        FileName = fileName.Trim();
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public string Name { get; }

    public string MimeType { get; }

    public string FileName { get; }

    public string Kind { get; init; } = "app";

    public string? Description { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IArtifactPayload Payload { get; }
}
