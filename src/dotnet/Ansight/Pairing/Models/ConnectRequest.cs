namespace Ansight.Pairing.Models;

public sealed class ConnectRequest
{
    public required string Type { get; set; }
    public required int Ver { get; set; }
    public required string ConfigId { get; set; }
    public required string OneTimeToken { get; set; }
    public required string AppId { get; set; }
    public required string ClientName { get; set; }
}
