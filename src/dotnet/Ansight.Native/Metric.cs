namespace Ansight;

/// <summary>
/// Represents a single sampled metric value captured by Ansight.
/// </summary>
public class Metric : IComparable<Metric>
{
    public required long Value { get; init; }
    
    public required DateTime CapturedAtUtc { get; init;}
    
    public required byte Channel { get; init; }

    public int CompareTo(Metric? other)
    {
        if (other is null)
        {
            return 1;
        }
        
        return DateTime.Compare(CapturedAtUtc, other.CapturedAtUtc);
    }
}
