using Ansight.Screenshot;

namespace Ansight.UnitTests;

public sealed class FixedRateCaptureScheduleTests
{
    [Fact]
    public void ScheduleStartsImmediatelyAndMaintainsStartToStartCadence()
    {
        var schedule = new FixedRateCaptureSchedule(
            TimeSpan.FromMilliseconds(750),
            initialTimestamp: 1_000,
            timestampFrequency: 1_000);

        Assert.Equal(TimeSpan.Zero, schedule.GetDelay(currentTimestamp: 1_000));

        var missedIntervals = schedule.Advance(currentTimestamp: 1_250);

        Assert.Equal(0, missedIntervals);
        Assert.Equal(1_750, schedule.NextCaptureTimestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(500), schedule.GetDelay(currentTimestamp: 1_250));
    }

    [Fact]
    public void ScheduleSkipsMissedDeadlinesWithoutRequestingACatchUpBurst()
    {
        var schedule = new FixedRateCaptureSchedule(
            TimeSpan.FromMilliseconds(750),
            initialTimestamp: 1_000,
            timestampFrequency: 1_000);

        var missedIntervals = schedule.Advance(currentTimestamp: 2_800);

        Assert.Equal(2, missedIntervals);
        Assert.Equal(3_250, schedule.NextCaptureTimestamp);
        Assert.Equal(TimeSpan.FromMilliseconds(450), schedule.GetDelay(currentTimestamp: 2_800));
    }
}
