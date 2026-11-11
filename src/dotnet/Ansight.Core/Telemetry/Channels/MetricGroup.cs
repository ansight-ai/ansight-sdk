using System.Text.Json.Serialization;

namespace Ansight.Telemetry.Channels;

public sealed record MetricGroup
{
    public MetricGroup(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id.Trim();
        Name = name.Trim();
    }

    [JsonPropertyName("id")]
    public string Id { get; }
    [JsonPropertyName("name")]
    public string Name { get; }
}
