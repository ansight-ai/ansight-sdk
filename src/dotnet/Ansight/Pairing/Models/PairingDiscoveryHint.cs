namespace Ansight.Pairing.Models;

public sealed class PairingDiscoveryHint
{
    public const string SchemaName = "ansight.discovery-hint.v1";

    public required string Schema { get; set; } = SchemaName;
    public string? Source { get; set; }
    public string? HostAddress { get; set; }
    public string? HostName { get; set; }
    public string? WifiName { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
}
