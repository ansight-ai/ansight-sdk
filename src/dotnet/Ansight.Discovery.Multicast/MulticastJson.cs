using System.Text.Json;

namespace Ansight.Discovery.Multicast;

public static class MulticastJson
{
    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
