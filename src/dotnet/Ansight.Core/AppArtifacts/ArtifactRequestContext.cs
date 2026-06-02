namespace Ansight.Artifacts;

/// <summary>
/// Context passed to artifact providers while creating an artifact.
/// </summary>
/// <param name="ToolRequestId">Tool protocol request id that initiated the artifact creation.</param>
/// <param name="SessionId">Optional live session id supplied by the remote caller.</param>
/// <param name="RequestedAtUtc">UTC timestamp for the request.</param>
public sealed record ArtifactRequestContext(
    string ToolRequestId,
    string? SessionId,
    DateTimeOffset RequestedAtUtc);
