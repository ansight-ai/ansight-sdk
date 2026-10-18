namespace Ansight.Artifacts;

using Ansight.Pairing;

/// <summary>Publishes app-selected files and snapshots into the connected host session.</summary>
public static class ArtifactPublisher
{
    /// <summary>
    /// Streams an artifact without a provider registration or remote tool request.
    /// Success means the host acknowledged retaining the session artifact. Requires a live
    /// connection and a host supporting artifact publication; disconnected artifacts are not queued.
    /// The SDK opens and disposes the payload stream. Cancellation is propagated to the caller.
    /// </summary>
    public static Task<OperationResult> PublishAsync(
        ArtifactResult artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        cancellationToken.ThrowIfCancellationRequested();
        return Runtime.IsInitialized
            ? Runtime.MutableInstance.PublishArtifactAsync(artifact, cancellationToken)
            : Task.FromResult(OperationResult.FromFailure("Initialize Ansight before publishing an artifact."));
    }
}
