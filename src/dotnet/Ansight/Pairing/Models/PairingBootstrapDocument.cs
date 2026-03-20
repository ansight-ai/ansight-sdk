namespace Ansight.Pairing.Models;

public sealed class PairingBootstrapDocument
{
    public const string SchemaName = "ansight.pairing-bootstrap.v1";

    public required string Schema { get; set; } = SchemaName;
    public required PairingConfig PairingConfig { get; set; }
    public PairingDiscoveryHint? Discovery { get; set; }
    public PairingConnectionHint? ConnectionHint { get; set; }
}
