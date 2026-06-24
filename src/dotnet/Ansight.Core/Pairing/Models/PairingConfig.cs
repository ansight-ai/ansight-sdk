namespace Ansight.Pairing.Models;

/// <summary>
/// Signed host-issued pairing configuration used to authorize and describe a pairing session.
/// </summary>
public sealed class PairingConfig
{
    /// <summary>
    /// Current schema identifier for signed pairing config payloads.
    /// </summary>
    public const string SchemaName = "ansight.pairing-config.v1";

    /// <summary>
    /// Schema identifier for the config payload.
    /// </summary>
    public required string Schema { get; set; }

    /// <summary>
    /// Stable identifier for this pairing config.
    /// </summary>
    public required string ConfigId { get; set; }

    /// <summary>
    /// App identifier the config targets.
    /// </summary>
    public required string AppId { get; set; }

    /// <summary>
    /// Human-readable application name the config targets.
    /// </summary>
    public required string AppName { get; set; }

    /// <summary>
    /// Time at which the host issued the config.
    /// </summary>
    public required DateTimeOffset IssuedAt { get; set; }

    /// <summary>
    /// Time after which the config is no longer valid.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// One-time token used to authorize the connection attempt.
    /// </summary>
    public required string OneTimeToken { get; set; }

    /// <summary>
    /// Host identity and transport metadata.
    /// </summary>
    public required PairingHost Host { get; set; }

    /// <summary>
    /// Challenge metadata used during the trust handshake.
    /// </summary>
    public required PairingChallenge Challenge { get; set; }

    /// <summary>
    /// Host signature covering the canonical pairing config payload.
    /// </summary>
    public required string Signature { get; set; }
}
