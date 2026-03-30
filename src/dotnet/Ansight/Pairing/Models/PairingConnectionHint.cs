namespace Ansight.Pairing.Models;

/// <summary>
/// Lightweight connection data embedded in QR and bootstrap payloads.
/// </summary>
public sealed class PairingConnectionHint
{
    /// <summary>
    /// Schema identifier for pairing connection hint payloads.
    /// </summary>
    public const string SchemaName = "ansight.pairing-connection-hint.v1";

    /// <summary>
    /// Schema identifier for this hint.
    /// </summary>
    public required string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Optional human-readable origin label for the payload.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Stable identifier for the referenced pairing config.
    /// </summary>
    public required string ConfigId { get; set; }

    /// <summary>
    /// Time at which the referenced config or hint was issued.
    /// </summary>
    public required DateTimeOffset IssuedAt { get; set; }

    /// <summary>
    /// Time after which the hint should no longer be used.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// One-time token the client should use when connecting.
    /// </summary>
    public required string OneTimeToken { get; set; }

    /// <summary>
    /// Challenge metadata the client should use when connecting.
    /// </summary>
    public required PairingChallenge Challenge { get; set; }
}
