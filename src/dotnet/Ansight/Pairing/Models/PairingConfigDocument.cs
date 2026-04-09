namespace Ansight.Pairing.Models;

/// <summary>
/// Self-contained pairing config payload used by file, QR, and bundled pairing flows.
/// </summary>
public sealed class PairingConfigDocument
{
    /// <summary>
    /// Current schema identifier for pairing config document payloads.
    /// </summary>
    public const string SchemaName = "ansight.pairing-config-document.v1";

    /// <summary>
    /// Legacy schema identifier accepted for backwards compatibility.
    /// </summary>
    public const string LegacySchemaName = "ansight.pairing-ticket.v1";

    /// <summary>
    /// Schema identifier for this config document.
    /// </summary>
    public string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Signed pairing config carried by the document.
    /// </summary>
    public required PairingConfig Config { get; set; }

    /// <summary>
    /// Discovery metadata that helps the client reach the host.
    /// </summary>
    public PairingDiscoveryHint? Discovery { get; set; }
}
