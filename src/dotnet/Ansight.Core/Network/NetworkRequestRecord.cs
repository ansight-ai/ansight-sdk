namespace Ansight.Network;

/// <summary>
/// Metadata captured for one completed HTTP request.
/// Request and response bodies are optional and bounded by the app-configured capture limit.
/// </summary>
public sealed record class NetworkRequestRecord
{
    /// <summary>
    /// V1 network record schema name.
    /// </summary>
    public const string SchemaName = "ansight.network-request.v1";

    /// <summary>
    /// Record schema.
    /// </summary>
    public string Schema { get; init; } = SchemaName;

    /// <summary>
    /// Unique request identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// SDK integration that observed the request.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// UTC request start time.
    /// </summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>
    /// UTC completion time, including failed or cancelled requests.
    /// </summary>
    public required DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>
    /// Monotonic elapsed duration in milliseconds.
    /// </summary>
    public required double DurationMilliseconds { get; init; }

    /// <summary>
    /// HTTP method.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Sanitized absolute or relative request URL.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Whether sensitive values in this captured record should be redacted by downstream hosts.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool RedactSensitiveData { get; init; } = true;

    /// <summary>
    /// Negotiated HTTP protocol version when available.
    /// </summary>
    public string? Protocol { get; init; }

    /// <summary>
    /// Sanitized request headers. Sensitive header values are redacted.
    /// </summary>
    public IReadOnlyList<NetworkHeader> RequestHeaders { get; init; } = Array.Empty<NetworkHeader>();

    /// <summary>
    /// Request body size reported by Content-Length, when known.
    /// </summary>
    public long? RequestBodySizeBytes { get; init; }

    /// <summary>
    /// Optional bounded request body captured by an explicitly installed network integration.
    /// </summary>
    public NetworkBody? RequestBody { get; init; }

    /// <summary>
    /// HTTP response status code, or <see langword="null"/> when no response was received.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// HTTP response reason phrase when available.
    /// </summary>
    public string? ReasonPhrase { get; init; }

    /// <summary>
    /// Sanitized response headers. Sensitive header values are redacted.
    /// </summary>
    public IReadOnlyList<NetworkHeader> ResponseHeaders { get; init; } = Array.Empty<NetworkHeader>();

    /// <summary>
    /// Response body size reported by Content-Length, when known.
    /// </summary>
    public long? ResponseBodySizeBytes { get; init; }

    /// <summary>
    /// Optional bounded response body captured by an explicitly installed network integration.
    /// </summary>
    public NetworkBody? ResponseBody { get; init; }

    /// <summary>
    /// Exception type for a failed request.
    /// </summary>
    public string? ErrorType { get; init; }

    /// <summary>
    /// Truncated exception message for a failed request.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
