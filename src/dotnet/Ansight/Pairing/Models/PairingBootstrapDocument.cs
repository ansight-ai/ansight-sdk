namespace Ansight.Pairing.Models;

/// <summary>
/// Persisted pairing document that combines a signed pairing config with optional discovery and connection hints.
/// </summary>
public sealed class PairingBootstrapDocument
{
    /// <summary>
    /// Schema identifier for pairing bootstrap documents.
    /// </summary>
    public const string SchemaName = "ansight.pairing-bootstrap.v1";

    /// <summary>
    /// Schema identifier for this document.
    /// </summary>
    public required string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Signed trust-anchor pairing config embedded in the bootstrap document.
    /// </summary>
    public required PairingConfig PairingConfig { get; set; }

    /// <summary>
    /// Optional discovery data captured alongside the config.
    /// </summary>
    public PairingDiscoveryHint? Discovery { get; set; }

    /// <summary>
    /// Optional connection hint that can override transient fields such as token or expiry.
    /// </summary>
    public PairingConnectionHint? ConnectionHint { get; set; }
}
