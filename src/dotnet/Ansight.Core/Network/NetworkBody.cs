namespace Ansight.Network;

/// <summary>
/// A bounded request or response body captured for network inspection.
/// </summary>
public sealed record class NetworkBody
{
    public const string Utf8Encoding = "utf8";
    public const string Base64Encoding = "base64";

    /// <summary>
    /// Media type reported by the HTTP content, when available.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// <c>utf8</c> for text or <c>base64</c> for binary data.
    /// </summary>
    public required string Encoding { get; init; }

    /// <summary>
    /// Captured body data in the declared encoding.
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// Number of decoded bytes retained in <see cref="Data"/>.
    /// </summary>
    public required long CapturedBytes { get; init; }

    /// <summary>
    /// Complete decoded body size when known.
    /// </summary>
    public long? TotalBytes { get; init; }

    /// <summary>
    /// Whether the complete body was larger than the retained prefix.
    /// </summary>
    public required bool Truncated { get; init; }
}
