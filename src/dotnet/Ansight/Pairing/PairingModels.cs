using System.Net;

namespace Ansight.Pairing;

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
    public int WsPort { get; set; } = PairingProtocolDefaults.WebSocketPort;
    public string WsPath { get; set; } = PairingProtocolDefaults.WebSocketPath;
    public int DiscoveryPort { get; set; } = PairingProtocolDefaults.DiscoveryPort;
    public string MdnsService { get; set; } = PairingProtocolDefaults.MdnsService;
    public required string HostPubKey { get; set; }
    public required string HostPubKeyFingerprint { get; set; }
}

public sealed class PairingTrust
{
    public required string Mode { get; set; }
    public required bool RequireTokenOnFirstPair { get; set; }
    public required bool AllowLanDiscovery { get; set; }
}

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

public sealed class PairingBootstrapDocument
{
    public const string SchemaName = "ansight.pairing-bootstrap.v1";

    public required string Schema { get; set; } = SchemaName;
    public required PairingConfig PairingConfig { get; set; }
    public PairingDiscoveryHint? Discovery { get; set; }
}

public enum PairingDiscoveryMode
{
    ConfiguredStrategy = 0,
    AutomaticMulticast = 0,
    BasicManual = 1
}

public sealed class PairingConnectionOptions
{
    public PairingDiscoveryMode DiscoveryMode { get; set; } = PairingDiscoveryMode.ConfiguredStrategy;
    public string? ManualHostAddress { get; set; }
}

public interface IPairingHostDiscoveryStrategy
{
    Task<IPAddress?> DiscoverHostAsync(PairingConfig config, CancellationToken cancellationToken);
}

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

public sealed class ParsedPairingDocument
{
    public required PairingConfig Config { get; init; }
    public PairingDiscoveryHint? DiscoveryHint { get; init; }
}
