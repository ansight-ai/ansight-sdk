using System.Text.Json.Serialization;

namespace Ansight.Pairing.Models;

/// <summary>
/// QR-issued enrollment invite used to register and reconnect an app instance.
/// </summary>
public sealed class PairingConfig
{
    /// <summary>
    /// Current schema identifier for Studio enrollment invites.
    /// </summary>
    public const string SchemaName = "ansight.enrollment-invite.v2";

    /// <summary>
    /// App identifier used by a generic one-use invite. Studio binds the grant
    /// to the actual app identifier supplied by the scanning installation.
    /// </summary>
    public const string AnyAppId = "*";

    /// <summary>
    /// Schema identifier for the config payload.
    /// </summary>
    public required string Schema { get; set; }

    /// <summary>
    /// Stable identifier for this enrollment invite.
    /// </summary>
    [JsonPropertyName("inviteId")]
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
    /// Minimum pairing protocol version accepted by this config.
    /// </summary>
    public int MinProtocolVersion { get; set; } = 2;

    /// <summary>
    /// Transport names permitted by this config.
    /// </summary>
    public string[] AllowedTransports { get; set; } = ["ws"];

    /// <summary>
    /// Host identity and transport metadata.
    /// </summary>
    public required PairingHost Host { get; set; }

    /// <summary>
    /// One-use access token and registration lifetime.
    /// </summary>
    public PairingEnrollment? Enrollment { get; set; }

}
