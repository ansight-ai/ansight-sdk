namespace Ansight.Pairing.Models;

public sealed class PairingConnectionHint
{
    public const string SchemaName = "ansight.pairing-connection-hint.v1";

    public required string Schema { get; set; } = SchemaName;
    public string? Source { get; set; }
    public required string ConfigId { get; set; }
    public required DateTimeOffset IssuedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string OneTimeToken { get; set; }
    public required PairingChallenge Challenge { get; set; }
}
