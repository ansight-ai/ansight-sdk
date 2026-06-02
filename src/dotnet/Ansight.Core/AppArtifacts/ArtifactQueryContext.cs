namespace Ansight.Artifacts;

/// <summary>
/// Context passed to artifact providers while querying available artifacts.
/// </summary>
/// <param name="ToolRequestId">Tool protocol request id that initiated the query.</param>
/// <param name="SessionId">Optional live session id supplied by the remote caller.</param>
/// <param name="QueriedAtUtc">UTC timestamp for the query.</param>
public sealed record ArtifactQueryContext(
    string ToolRequestId,
    string? SessionId,
    DateTimeOffset QueriedAtUtc);
