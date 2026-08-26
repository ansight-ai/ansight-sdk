namespace Ansight.Network;

/// <summary>
/// App-side privacy controls applied before a network request enters Ansight capture.
/// Known credential fields are always redacted, regardless of these options.
/// </summary>
public sealed class NetworkRequestSanitizationOptions
{
    /// <summary>
    /// Whether credential-bearing headers, query parameters, and text values are redacted.
    /// Defaults to <see langword="true"/>. Disable only for an explicitly trusted local capture.
    /// </summary>
    public bool RedactSensitiveData { get; init; } = true;

    /// <summary>
    /// Whether sanitized request headers are retained. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeRequestHeaders { get; init; } = true;

    /// <summary>
    /// Whether sanitized response headers are retained. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeResponseHeaders { get; init; } = true;

    /// <summary>
    /// Whether the sanitized URL query string is retained. Set to <see langword="false"/> to strip it entirely.
    /// </summary>
    public bool IncludeQueryString { get; init; } = true;

    /// <summary>
    /// Whether declared request and response body sizes are retained.
    /// </summary>
    public bool IncludeBodySizes { get; init; } = true;

    /// <summary>
    /// Whether a bounded request body may be captured. Defaults to <see langword="true"/>.
    /// </summary>
    public bool CaptureRequestBody { get; init; } = true;

    /// <summary>
    /// Whether a bounded response body may be captured. Defaults to <see langword="true"/>.
    /// </summary>
    public bool CaptureResponseBody { get; init; } = true;

    /// <summary>
    /// Maximum decoded bytes retained per body. Defaults to 64 KiB; larger explicit limits are honored.
    /// </summary>
    public int MaximumBodyBytes { get; init; } = 64 * 1024;

    /// <summary>
    /// Whether non-text content may be retained as Base64. Defaults to <see langword="false"/>.
    /// </summary>
    public bool CaptureBinaryBodies { get; init; }

    /// <summary>
    /// App-specific header names whose values should be replaced with <c>&lt;redacted&gt;</c>.
    /// </summary>
    public IReadOnlyCollection<string> AdditionalSensitiveHeaderNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// App-specific URL query parameter names whose values should be replaced with <c>&lt;redacted&gt;</c>.
    /// </summary>
    public IReadOnlyCollection<string> AdditionalSensitiveQueryParameterNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional app callback that receives an already-sanitized URL and may replace it.
    /// </summary>
    public Func<string, string>? UrlSanitizer { get; init; }

    /// <summary>
    /// Optional final app callback. Return a replacement record, or <see langword="null"/> to suppress capture.
    /// Mandatory sanitization is applied again to any returned record.
    /// </summary>
    public Func<NetworkRequestRecord, NetworkRequestRecord?>? RequestSanitizer { get; init; }
}
