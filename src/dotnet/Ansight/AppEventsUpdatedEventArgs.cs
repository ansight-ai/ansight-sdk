namespace Ansight;

/// <summary>
/// Provides details about events added to or removed from the data sink.
/// </summary>
public class AppEventsUpdatedEventArgs : EventArgs
{
    public AppEventsUpdatedEventArgs(IReadOnlyList<AppEvent> added, IReadOnlyList<AppEvent> removed)
    {
        Added = added ?? throw new ArgumentNullException(nameof(added));
        Removed = removed ?? throw new ArgumentNullException(nameof(removed));
    }

    /// <summary>
    /// Events that have been appended since the last update.
    /// </summary>
    public IReadOnlyList<AppEvent> Added { get; }
    
    /// <summary>
    /// Events that have been pruned since the last update.
    /// </summary>
    public IReadOnlyList<AppEvent> Removed { get; }
}
