namespace Ansight;

/// <summary>
/// Represents a single annotated moment in Ansight, carrying metadata used for rendering markers and event lists.
/// </summary>
public class AppEvent : IComparable<AppEvent>
{
    public AppEvent(string label,
                      AppEventType type,
                      string details,
                      DateTime capturedAtUtc,
                      object? externalId,
                      byte channel)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

        Label = label;
        Type = type;
        Details = details ?? string.Empty;
        CapturedAtUtc = capturedAtUtc;
        ExternalId = externalId;
        Channel = channel;
        Id = Guid.CreateVersion7();
    }

    public string Label {get;}
    
    public AppEventType Type { get; }

    public string Details { get; }

    /// <summary>
    /// The date-time that this event was captured, in UTC time.
    /// </summary>
    public DateTime CapturedAtUtc {get; }
    
    /// <summary>
    /// The internal Ansight ID of this event.
    /// </summary>
    public Guid Id {get;}
    
    /// <summary>
    /// An optional ID that you may use to identify this event to something within the outer application.
    /// </summary>
    public object? ExternalId { get;}
    
    /// <summary>
    /// The <see cref="Channel"/> that this event is connected to.
    /// </summary>
    public byte Channel {get;}

    public int CompareTo(AppEvent? other)
    {
        return other is null ? 1 : DateTime.Compare(CapturedAtUtc,  other.CapturedAtUtc);
    }
}
