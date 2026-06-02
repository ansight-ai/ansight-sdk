namespace Ansight.Artifacts;

/// <summary>
/// Artifact created by an app provider.
/// </summary>
/// <param name="Metadata">Artifact metadata returned to the remote caller.</param>
/// <param name="Payload">Artifact payload source streamed by the SDK.</param>
public sealed record ArtifactResult(
    ArtifactMetadata Metadata,
    IArtifactPayload Payload);
