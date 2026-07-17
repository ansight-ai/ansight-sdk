namespace Ansight.Annotations;

using System.Text.Json.Nodes;

/// <summary>
/// Mutable contribution context passed to annotation capture hooks.
/// </summary>
public sealed class AnnotationCaptureContext
{
    private readonly Dictionary<string, JsonNode?> customData = new(StringComparer.Ordinal);
    private readonly List<AnnotationArtifact> artifacts = [];
    private readonly List<string> hookFailures = [];

    internal AnnotationCaptureContext(
        Guid annotationId,
        DateTimeOffset requestedAtUtc,
        AnnotationCaptureRequest request,
        IReadOnlyList<AnnotationEvidenceResult> evidence)
    {
        AnnotationId = annotationId;
        CapturedAtUtc = requestedAtUtc;
        Request = request;
        Evidence = evidence;

        foreach (var item in request.CustomData)
        {
            customData[item.Key] = item.Value?.DeepClone();
        }
    }

    public Guid AnnotationId { get; }

    /// <summary>
    /// Time at which annotation capture was requested, before evidence capture and editor presentation.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; }

    public AnnotationCaptureRequest Request { get; }

    public IReadOnlyList<AnnotationEvidenceResult> Evidence { get; }

    public void AddCustomData(string key, JsonNode? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        customData[key.Trim()] = value?.DeepClone();
    }

    public void AddArtifact(AnnotationArtifact artifact)
        => artifacts.Add(artifact ?? throw new ArgumentNullException(nameof(artifact)));

    internal IReadOnlyDictionary<string, JsonNode?> CustomData => customData;

    internal IReadOnlyList<AnnotationArtifact> Artifacts => artifacts;

    internal IReadOnlyList<string> HookFailures => hookFailures;

    internal void RecordHookFailure(IAnnotationCaptureHook hook, Exception exception)
    {
        hookFailures.Add($"{hook.GetType().FullName}: {exception.Message}");
    }
}
