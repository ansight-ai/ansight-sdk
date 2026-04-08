namespace Ansight.Pairing.Models;

/// <summary>
/// Self-contained pairing payload used by file, QR, and bundled pairing flows.
/// </summary>
public sealed class PairingTicket
{
    /// <summary>
    /// Schema identifier for pairing ticket payloads.
    /// </summary>
    public const string SchemaName = "ansight.pairing-ticket.v1";

    /// <summary>
    /// Schema identifier for this ticket.
    /// </summary>
    public string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Signed pairing config carried by the ticket.
    /// </summary>
    public required PairingConfig Config { get; set; }

    /// <summary>
    /// Discovery metadata that helps the client reach the host.
    /// </summary>
    public PairingDiscoveryHint? Discovery { get; set; }
}
