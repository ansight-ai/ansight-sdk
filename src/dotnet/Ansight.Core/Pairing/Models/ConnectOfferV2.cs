namespace Ansight.Pairing.Models;

/// <summary>
/// Host-signed, nonce-bound protocol-v2 UDP response advertising a pinned WSS endpoint.
/// </summary>
public sealed class ConnectOfferV2
{
    public const string MessageType = "CONNECT_OFFER_V2";

    public required string Type { get; set; }

    public required int Ver { get; set; }

    public required string RequestId { get; set; }

    public required string ConfigId { get; set; }

    public required string AppId { get; set; }

    public required string ClientNonce { get; set; }

    public required string HostNonce { get; set; }

    public required string HostId { get; set; }

    public required int SelectedVersion { get; set; }

    public required string SelectedTransport { get; set; }

    public required int WebSocketPort { get; set; }

    public required string WebSocketPath { get; set; }

    public required string TlsSpkiSha256 { get; set; }

    public required string ExpiresAt { get; set; }

    public required string SignatureAlgorithm { get; set; }

    public required string Signature { get; set; }
}
