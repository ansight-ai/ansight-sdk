using System.Net;

namespace Ansight.Discovery.Multicast;

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
