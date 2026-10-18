namespace Ansight.Artifacts;

/// <summary>Live app-initiated artifact transfer contract.</summary>
public static class ArtifactPublishProtocol
{
    public const string Schema = "ansight.artifact.publish.v1";
    public const string BeginAction = "artifact.publish.begin";
    public const string CommitAction = "artifact.publish.commit";
    public const string AbortAction = "artifact.publish.abort";
    public const int ChunkBytes = 64 * 1024;
    public const long MaximumSizeBytes = 512L * 1024 * 1024;
}
