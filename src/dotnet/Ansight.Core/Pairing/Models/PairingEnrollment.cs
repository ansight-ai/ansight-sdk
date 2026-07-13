namespace Ansight.Pairing.Models;

/// <summary>
/// Single-use protocol-v2 enrollment material issued inside a signed pairing config.
/// The secret is presented only as an HMAC proof inside the pinned TLS channel.
/// </summary>
public sealed class PairingEnrollment
{
    public const string HmacSha256 = "HMAC-SHA256";

    public required string TicketId { get; set; }

    public required string Secret { get; set; }

    public required string ExpiresAt { get; set; }

    public required string GrantExpiresAt { get; set; }

    public int MaxUses { get; set; } = 1;

    public string[] MaxScopes { get; set; } = [];

    public bool AllowCritical { get; set; }
}
