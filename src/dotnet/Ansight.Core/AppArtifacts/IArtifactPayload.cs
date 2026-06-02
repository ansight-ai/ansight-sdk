namespace Ansight.Artifacts;

/// <summary>
/// Stream-oriented artifact payload source.
/// </summary>
public interface IArtifactPayload
{
    /// <summary>
    /// Payload size in bytes, when known before opening the stream.
    /// </summary>
    long? SizeBytes { get; }

    /// <summary>
    /// Opens a fresh readable stream for the artifact payload.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for asynchronous stream creation.</param>
    /// <returns>A readable stream. The SDK owns disposal of the returned stream.</returns>
    ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}
