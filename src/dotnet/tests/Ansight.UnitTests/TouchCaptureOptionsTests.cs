using Ansight.Input;

namespace Ansight.UnitTests;

public sealed class TouchCaptureOptionsTests
{
    [Fact]
    public void WithTouchCapture_ConfiguresCaptureOptions()
    {
        var options = Options.CreateBuilder()
            .WithTouchCapture(captureMoveEvents: false, captureCancelEvents: true)
            .Build();

        Assert.NotNull(options.TouchCapture);
        Assert.False(options.TouchCapture.CaptureMoveEvents);
        Assert.True(options.TouchCapture.CaptureCancelEvents);
    }

    [Fact]
    public void WithTouchCapture_ClonesProvidedOptions()
    {
        var source = new TouchCaptureOptions
        {
            CaptureMoveEvents = false,
            CaptureCancelEvents = false
        };

        var options = Options.CreateBuilder()
            .WithTouchCapture(source)
            .Build();

        source.CaptureMoveEvents = true;
        source.CaptureCancelEvents = true;

        Assert.NotNull(options.TouchCapture);
        Assert.False(options.TouchCapture.CaptureMoveEvents);
        Assert.False(options.TouchCapture.CaptureCancelEvents);
    }

    [Fact]
    public void WithoutTouchCapture_RemovesConfiguredCapture()
    {
        var options = Options.CreateBuilder()
            .WithTouchCapture()
            .WithoutTouchCapture()
            .Build();

        Assert.Null(options.TouchCapture);
    }

    [Fact]
    public void TouchCapture_DoesNotAddTelemetryChannels()
    {
        var options = Options.CreateBuilder()
            .WithTouchCapture()
            .Build();

        var dataSink = new MutableDataSink(options);

        Assert.DoesNotContain(dataSink.Channels, channel => channel.Name.Contains("Touch", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TouchCaptureHub_IgnoresRecordsWhenDisabled()
    {
        var hub = new TouchCaptureHub(options: null);
        var captured = 0;
        hub.TouchCaptured += (_, _) => captured++;

        hub.Record(new CapturedTouch(
            CapturedTouchAction.Down,
            pointerId: 1,
            pointerIndex: 0,
            pointerCount: 1,
            x: 10,
            y: 20,
            surfaceWidth: 100,
            surfaceHeight: 200,
            coordinateUnit: "pixels",
            surfaceScale: 1,
            DateTimeOffset.UtcNow));

        Assert.Equal(0, captured);
    }
}
