namespace Ansight;

/// <summary>
/// Provides details about metric samples added to or removed from the data sink.
/// </summary>
public class MetricsUpdatedEventArgs : EventArgs
{
    public MetricsUpdatedEventArgs(IReadOnlyList<Metric> added, IReadOnlyList<Metric> removed)
    {
        Added = added ?? throw new ArgumentNullException(nameof(added));
        Removed = removed ?? throw new ArgumentNullException(nameof(removed));
    }

    /// <summary>
    /// Metric samples appended since the last update.
    /// </summary>
    public IReadOnlyList<Metric> Added { get; }
    
    /// <summary>
    /// Metric samples pruned since the last update.
    /// </summary>
    public IReadOnlyList<Metric> Removed { get; }
}
