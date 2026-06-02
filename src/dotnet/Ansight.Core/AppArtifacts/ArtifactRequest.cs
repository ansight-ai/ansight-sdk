namespace Ansight.Artifacts;

/// <summary>
/// Request passed to an artifact provider to create a specific artifact.
/// </summary>
/// <param name="ProviderId">Provider id selected by the caller.</param>
/// <param name="ArtifactId">Artifact id selected by the caller.</param>
/// <param name="Arguments">Provider-specific request arguments.</param>
/// <param name="Context">Request context supplied by the SDK.</param>
public sealed record ArtifactRequest(
    string ProviderId,
    string ArtifactId,
    IReadOnlyDictionary<string, string> Arguments,
    ArtifactRequestContext Context);
