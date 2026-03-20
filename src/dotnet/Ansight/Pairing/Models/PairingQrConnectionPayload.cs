namespace Ansight.Pairing.Models;

public sealed class PairingQrConnectionPayload
{
    public const string SchemaName = "ansight.qr-pairing-connection.v1";

    public required string Schema { get; set; } = SchemaName;
    public required PairingConnectionHint Connection { get; set; }
    public PairingDiscoveryHint? Discovery { get; set; }
}
