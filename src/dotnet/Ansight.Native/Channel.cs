namespace Ansight;

/// <summary>
/// Defines a metric/event channel rendered by Ansight, including its display name and colour.
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
    /// Human-readable channel label shown in the UI.
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// Colour used when rendering the channel.
    /// </summary>
    public Color Color { get; }
}
