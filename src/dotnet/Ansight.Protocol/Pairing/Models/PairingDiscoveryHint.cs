namespace Ansight.Pairing.Models;

/// <summary>
/// Discovery metadata that helps the client find or label a host before connecting.
/// </summary>
public sealed class PairingDiscoveryHint
{
    /// <summary>
    /// Schema identifier for discovery hint payloads.
    /// </summary>
    public const string SchemaName = "ansight.discovery-hint.v1";

    /// <summary>
    /// Schema identifier for this hint.
    /// </summary>
    public required string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Optional human-readable source label for the hint.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Candidate host addresses that can be used for the connection attempt.
    /// </summary>
    public string[]? HostAddresses { get; set; }

    /// <summary>
    /// Optional UDP discovery port advertised for the connection bootstrap.
    /// </summary>
    public int? DiscoveryPort { get; set; }

    /// <summary>
    /// Human-readable host name captured when the hint was created.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// Wi-Fi network name captured when the hint was created, when available.
    /// </summary>
    public string? WifiName { get; set; }

    /// <summary>
    /// Time at which the discovery metadata was captured.
    /// </summary>
    public DateTimeOffset? CapturedAt { get; set; }
}
