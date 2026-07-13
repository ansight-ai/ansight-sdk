namespace Ansight.Pairing.Models;

public sealed class AuthChallengeV2
{
    public const string MessageType = "AUTH_CHALLENGE_V2";

    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required string AuthSessionId { get; set; }
    public required string RequestId { get; set; }
    public required string ConfigId { get; set; }
    public required string AppId { get; set; }
    public required string ClientNonce { get; set; }
    public required string HostNonce { get; set; }
    public required string ServerChallenge { get; set; }
    public required string ExpiresAt { get; set; }
}

public sealed class AuthEnrollV2
{
    public const string MessageType = "AUTH_ENROLL_V2";

    public string Type { get; set; } = MessageType;
    public int Ver { get; set; } = 2;
    public required string AuthSessionId { get; set; }
    public required string TicketId { get; set; }
    public required string ClientKeyId { get; set; }
    public required string ClientPublicKey { get; set; }
    public string[] RequestedScopes { get; set; } = [];
    public bool RequestCritical { get; set; }
    public string ProofAlgorithm { get; set; } = PairingEnrollment.HmacSha256;
    public required string Proof { get; set; }
}

public sealed class AuthProveV2
{
    public const string MessageType = "AUTH_PROVE_V2";

    public string Type { get; set; } = MessageType;
    public int Ver { get; set; } = 2;
    public required string AuthSessionId { get; set; }
    public required string GrantId { get; set; }
    public required string ClientKeyId { get; set; }
    public string SignatureAlgorithm { get; set; } = PairingV2Crypto.SignatureAlgorithm;
    public required string Signature { get; set; }
}

public sealed class AuthOkV2
{
    public const string MessageType = "AUTH_OK_V2";

    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required string SessionId { get; set; }
    public required PairingGrantV2 Grant { get; set; }
}

public sealed class PairingGrantV2
{
    public required string GrantId { get; set; }
    public required string HostId { get; set; }
    public required string ConfigId { get; set; }
    public required string AppId { get; set; }
    public required string ClientKeyId { get; set; }
    public string[] AllowedScopes { get; set; } = [];
    public bool AllowCritical { get; set; }
    public required string IssuedAt { get; set; }
    public required string ExpiresAt { get; set; }
    public required string SignatureAlgorithm { get; set; }
    public required string Signature { get; set; }
}

public sealed class AuthErrorV2
{
    public const string MessageType = "AUTH_ERROR_V2";

    public required string Type { get; set; }
    public int Ver { get; set; } = 2;
    public required string Code { get; set; }
    public required string Message { get; set; }
    public bool Retryable { get; set; }
}
