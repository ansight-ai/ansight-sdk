namespace Ansight.Pairing.Models;

/// <summary>
/// Combined QR payload that carries connection data plus optional discovery metadata.
/// </summary>
public sealed class PairingQrConnectionPayload
{
    /// <summary>
    /// Schema identifier for QR pairing connection payloads.
    /// </summary>
    public const string SchemaName = "ansight.qr-pairing-connection.v1";

    /// <summary>
    /// Schema identifier for this payload.
    /// </summary>
    public required string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Connection data required to initiate the pairing handshake.
    /// </summary>
    public required PairingConnectionHint Connection { get; set; }

    /// <summary>
    /// Optional discovery metadata that helps the client find the host.
    /// </summary>
    public PairingDiscoveryHint? Discovery { get; set; }
}
