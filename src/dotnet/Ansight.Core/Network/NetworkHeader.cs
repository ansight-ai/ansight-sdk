namespace Ansight.Network;

/// <summary>
/// A captured HTTP header after sensitive-value redaction.
/// </summary>
public sealed class NetworkHeader
{
    /// <summary>
    /// Header name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Header value, or <c>&lt;redacted&gt;</c> when the header may contain credentials.
    /// </summary>
    public required string Value { get; init; }
}
