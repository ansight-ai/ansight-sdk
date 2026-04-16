namespace Ansight.Pairing.Models;

/// <summary>
/// Build-time marker embedded when Ansight developer pairing is enabled without a pairing config.
/// </summary>
public sealed class DevelopmentPairingDocument
{
    /// <summary>
    /// Current schema identifier for developer pairing marker payloads.
    /// </summary>
    public const string SchemaName = "ansight.developer-pairing.v1";

    /// <summary>
    /// Schema identifier for this marker payload.
    /// </summary>
    public string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Discovery metadata that helps the client reach the local Studio host.
    /// </summary>
    public PairingDiscoveryHint? Discovery { get; set; }
}
