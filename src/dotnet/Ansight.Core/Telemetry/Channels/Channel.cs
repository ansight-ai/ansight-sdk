using System.Drawing;
namespace Ansight.Telemetry.Channels;

/// <summary>
/// Defines a metric/event channel used by Ansight.
/// </summary>
public class Channel
{
    public Channel(byte id, string name, Color color, string? unit = null, string type = "custom",
        string? source = null, string? group = null, string? kind = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(name));
        Id = id;
        Name = name;
        Color = color;
        Unit = unit?.Trim();
        Type = string.IsNullOrWhiteSpace(type) ? "custom" : type.Trim();
        Source = source?.Trim();
        Group = group?.Trim();
        Kind = kind?.Trim();
    }

    /// <summary>
    /// Numeric identifier for the channel; reserved IDs are defined in <see cref="Constants.ReservedChannels"/>.
    /// </summary>
    public byte Id { get; }

    /// <summary>
    /// Human-readable channel label.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Optional display color metadata associated with the channel.
    /// </summary>
    public Color Color { get; }

    public string? Unit { get; }
    public string Type { get; }
    public string? Source { get; }
    public string? Group { get; }
    public string? Kind { get; }
}
