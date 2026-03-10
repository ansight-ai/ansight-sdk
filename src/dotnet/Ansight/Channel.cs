using System.Drawing;
namespace Ansight;

/// <summary>
/// Defines a metric/event channel used by Ansight.
/// </summary>
public class Channel
{
    public Channel(byte id, string name, Color color)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(name));
        Id = id;
        Name = name;
        Color = color;
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
}
