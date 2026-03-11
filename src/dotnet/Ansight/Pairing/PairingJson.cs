using System.Text.Json;

namespace Ansight.Pairing;

public static class PairingJson
{
    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
