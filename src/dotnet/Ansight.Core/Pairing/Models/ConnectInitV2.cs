namespace Ansight.Pairing.Models;

/// <summary>
/// Secret-free protocol-v2 UDP bootstrap request.
/// </summary>
public sealed class ConnectInitV2
{
    public const string MessageType = "CONNECT_INIT_V2";

    public string Type { get; set; } = MessageType;

    public int Ver { get; set; } = 2;

    public required string RequestId { get; set; }

    public required string ConfigId { get; set; }

    public required string AppId { get; set; }

    public required string ClientNonce { get; set; }

    public int[] SupportedVersions { get; set; } = [2];

    public string[] SupportedTransports { get; set; } = ["wss"];

}
