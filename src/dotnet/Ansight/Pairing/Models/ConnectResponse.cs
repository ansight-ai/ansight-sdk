namespace Ansight.Pairing.Models;

public sealed class ConnectResponse
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required bool Accepted { get; set; }
    public required string Reason { get; set; }
    public string? ReasonMessage { get; set; }
    public required string HostId { get; set; }
    public required string HostName { get; set; }
    public required string Message { get; set; }
    public int? WebSocketPort { get; set; }
    public string? WebSocketPath { get; set; }
    public string? WebSocketToken { get; set; }
}
