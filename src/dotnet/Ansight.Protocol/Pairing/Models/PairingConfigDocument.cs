namespace Ansight.Pairing.Models;

/// <summary>
/// Self-contained enrollment invite used by QR scanning.
/// </summary>
public sealed class PairingConfigDocument
{
    /// <summary>
    /// Current schema identifier for pairing config document payloads.
    /// </summary>
    public const string SchemaName = "ansight.enrollment-invite-document.v2";

    /// <summary>
    /// Schema identifier for this config document.
    /// </summary>
    public string Schema { get; set; } = SchemaName;

    /// <summary>
    /// Enrollment invite carried by the document.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("invite")]
    public required PairingConfig Config { get; set; }

    /// <summary>
    /// Discovery metadata that helps the client reach the host.
    /// </summary>
    public PairingDiscoveryHint? Discovery { get; set; }
}
