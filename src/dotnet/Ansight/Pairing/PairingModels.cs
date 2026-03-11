using System.Net;

namespace Ansight.Pairing;

public sealed class ConnectRequest
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required string ConfigId { get; set; }
    public required string OneTimeToken { get; set; }
    public required string AppId { get; set; }
    public required string ClientName { get; set; }
}

public sealed class ConnectResponse
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required bool Accepted { get; set; }
    public required string Reason { get; set; }
    public required string HostId { get; set; }
    public required string HostName { get; set; }
    public required string Message { get; set; }
    public int? WebSocketPort { get; set; }
    public string? WebSocketPath { get; set; }
    public string? WebSocketToken { get; set; }
}

public sealed class DiscoverRequest
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required string Nonce { get; set; }
    public required string AppId { get; set; }
}

public sealed class DiscoverResponse
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public string? HostId { get; set; }
    public string? HostName { get; set; }
    public required int WsPort { get; set; }
    public required string WsPath { get; set; }
    public required string HostPubKey { get; set; }
    public required string RespNonce { get; set; }
    public required string Sig { get; set; }
}

public sealed record DiscoveredHost(IPAddress Address);

public sealed class PairingChallenge
{
    public required string Alg { get; set; }
    public required string ChallengePubKey { get; set; }
    public required bool RequireProofOnFirstPair { get; set; }
}

public sealed class PairingConfig
{
    public required string Schema { get; set; }
    public required string ConfigId { get; set; }
    public required string AppId { get; set; }
    public required string AppName { get; set; }
    public required DateTimeOffset IssuedAt { get; set; }
    public required DateTimeOffset ExpiresAt { get; set; }
    public required string OneTimeToken { get; set; }
    public required PairingHost Host { get; set; }
    public required PairingChallenge Challenge { get; set; }
    public required PairingTrust Trust { get; set; }
    public required string Signature { get; set; }
}

public sealed class PairingHost
{
    public string? HostId { get; set; }
    public string? HostName { get; set; }
    public required string HostPubKey { get; set; }
    public required string HostPubKeyFingerprint { get; set; }
}

public sealed class PairingTrust
{
    public required string Mode { get; set; }
    public required bool RequireTokenOnFirstPair { get; set; }
    public required bool AllowLanDiscovery { get; set; }
}
