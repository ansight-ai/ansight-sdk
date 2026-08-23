using System.Diagnostics;

namespace Ansight.Screenshot;

internal sealed class FixedRateCaptureSchedule
{
    private readonly long intervalTimestampTicks;
    private readonly long timestampFrequency;
    private long nextCaptureTimestamp;

    public FixedRateCaptureSchedule(TimeSpan interval)
        : this(interval, Stopwatch.GetTimestamp(), Stopwatch.Frequency)
    {
    }

    internal FixedRateCaptureSchedule(
        TimeSpan interval,
        long initialTimestamp,
        long timestampFrequency)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
        if (timestampFrequency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        }

        this.timestampFrequency = timestampFrequency;
        intervalTimestampTicks = Math.Max(
            1,
            (long)Math.Ceiling(interval.TotalSeconds * timestampFrequency));
        nextCaptureTimestamp = initialTimestamp;
    }

    internal long NextCaptureTimestamp => nextCaptureTimestamp;

    public TimeSpan GetDelay()
    {
        return GetDelay(Stopwatch.GetTimestamp());
    }

    internal TimeSpan GetDelay(long currentTimestamp)
    {
        var remainingTimestampTicks = nextCaptureTimestamp - currentTimestamp;
        return remainingTimestampTicks <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(remainingTimestampTicks / (double)timestampFrequency);
    }

    public int Advance()
    {
        return Advance(Stopwatch.GetTimestamp());
    }

    internal int Advance(long currentTimestamp)
    {
        var nextTimestamp = nextCaptureTimestamp + intervalTimestampTicks;
        long missedIntervals = 0;
        if (nextTimestamp <= currentTimestamp)
        {
            missedIntervals = ((currentTimestamp - nextTimestamp) / intervalTimestampTicks) + 1;
            nextTimestamp += missedIntervals * intervalTimestampTicks;
        }

        nextCaptureTimestamp = nextTimestamp;
        return missedIntervals >= int.MaxValue ? int.MaxValue : (int)missedIntervals;
    }
}
