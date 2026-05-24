using Ansight.Input;

namespace Ansight.UnitTests;

public sealed class TouchCaptureOptionsTests
{
    [Fact]
    public void OptionsDefault_EnableTouchCapture()
    {
        AssertDefaultTouchCapture(Options.Default.TouchCapture);
    }

    [Fact]
    public void BuilderDefault_EnableTouchCapture()
    {
        var options = Options.CreateBuilder().Build();

        AssertDefaultTouchCapture(options.TouchCapture);
    }

    [Fact]
    public void WithTouchCapture_ConfiguresCaptureOptions()
    {
        var options = Options.CreateBuilder()
            .WithTouchCapture(
                captureMoveEvents: false,
                captureCancelEvents: true,
                moveCaptureDistanceThreshold: 6.5d,
                moveCaptureFramesPerSecond: 24)
            .Build();

        Assert.NotNull(options.TouchCapture);
        Assert.False(options.TouchCapture.CaptureMoveEvents);
        Assert.True(options.TouchCapture.CaptureCancelEvents);
        Assert.Equal(6.5d, options.TouchCapture.MoveCaptureDistanceThreshold);
        Assert.Equal(24, options.TouchCapture.MoveCaptureFramesPerSecond);
    }

    [Fact]
    public void WithTouchCapture_ClonesProvidedOptions()
    {
        var source = new TouchCaptureOptions
        {
            CaptureMoveEvents = false,
            CaptureCancelEvents = false,
            MoveCaptureDistanceThreshold = 8,
            MoveCaptureFramesPerSecond = 12
        };

        var options = Options.CreateBuilder()
            .WithTouchCapture(source)
            .Build();

        source.CaptureMoveEvents = true;
        source.CaptureCancelEvents = true;
        source.MoveCaptureDistanceThreshold = 1;
        source.MoveCaptureFramesPerSecond = 60;

        Assert.NotNull(options.TouchCapture);
        Assert.False(options.TouchCapture.CaptureMoveEvents);
        Assert.False(options.TouchCapture.CaptureCancelEvents);
        Assert.Equal(8, options.TouchCapture.MoveCaptureDistanceThreshold);
        Assert.Equal(12, options.TouchCapture.MoveCaptureFramesPerSecond);
    }

    [Fact]
    public void WithTouchCapture_UsesNonFloodingMoveDefaults()
    {
        var options = Options.CreateBuilder()
            .WithTouchCapture()
            .Build();

        Assert.NotNull(options.TouchCapture);
        Assert.Equal(TouchCaptureOptions.DefaultMoveCaptureDistanceThreshold, options.TouchCapture.MoveCaptureDistanceThreshold);
        Assert.Equal(15, TouchCaptureOptions.DefaultMoveCaptureFramesPerSecond);
        Assert.Equal(TouchCaptureOptions.DefaultMoveCaptureFramesPerSecond, options.TouchCapture.MoveCaptureFramesPerSecond);
    }

    [Fact]
    public void WithoutTouchCapture_RemovesConfiguredCapture()
    {
        var options = Options.CreateBuilder()
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

        hub.Record(CreateTouch());

        Assert.Equal(0, captured);
    }

    [Fact]
    public void TouchCaptureHub_IgnoresRecordsWhenRuntimeCaptureDisabled()
    {
        var hub = new TouchCaptureHub(new TouchCaptureOptions());
        var captured = 0;
        hub.TouchCaptured += (_, _) => captured++;

        hub.DisableRuntimeCapture();
        hub.Record(CreateTouch());

        Assert.False(hub.IsRuntimeCaptureEnabled);
        Assert.Equal(0, captured);

        hub.EnableRuntimeCapture();
        hub.Record(CreateTouch());

        Assert.True(hub.IsRuntimeCaptureEnabled);
        Assert.Equal(1, captured);
    }

    [Fact]
    public void TouchCaptureHub_AppliesRuntimeCaptureGuardForEachRecord()
    {
        var hub = new TouchCaptureHub(new TouchCaptureOptions());
        var captureAllowed = true;
        var guardCalls = 0;
        var captured = 0;
        hub.TouchCaptured += (_, _) => captured++;
        hub.SetRuntimeCaptureGuard(() =>
        {
            guardCalls++;
            return captureAllowed;
        });

        hub.Record(CreateTouch());
        captureAllowed = false;
        hub.Record(CreateTouch());
        hub.SetRuntimeCaptureGuard(null);
        hub.Record(CreateTouch());

        Assert.Equal(2, guardCalls);
        Assert.Equal(2, captured);
    }

    [Fact]
    public void RuntimeImpl_ControlsTouchCaptureAtRuntime()
    {
        var runtime = new RuntimeImpl(Options.CreateBuilder().WithTouchCapture().Build());
        var captured = 0;
        runtime.TouchCaptureHub.TouchCaptured += (_, _) => captured++;

        runtime.DisableTouchCapture();
        runtime.TouchCaptureHub.Record(CreateTouch());

        Assert.False(runtime.IsTouchCaptureEnabled);
        Assert.Equal(0, captured);

        runtime.EnableTouchCapture();
        runtime.TouchCaptureHub.Record(CreateTouch());

        Assert.True(runtime.IsTouchCaptureEnabled);
        Assert.Equal(1, captured);
    }

    [Fact]
    public void RuntimeImpl_TouchCaptureGuardSuppressesRecords()
    {
        var runtime = new RuntimeImpl(Options.CreateBuilder().WithTouchCapture().Build());
        var captureAllowed = false;
        var captured = 0;
        runtime.TouchCaptureHub.TouchCaptured += (_, _) => captured++;
        runtime.SetTouchCaptureGuard(() => captureAllowed);

        runtime.TouchCaptureHub.Record(CreateTouch());
        captureAllowed = true;
        runtime.TouchCaptureHub.Record(CreateTouch());

        Assert.True(runtime.IsTouchCaptureEnabled);
        Assert.Equal(1, captured);
    }

    [Fact]
    public void RuntimeImpl_DoesNotEnableTouchCaptureWhenTouchCaptureWasNotConfigured()
    {
        var runtime = new RuntimeImpl(Options.CreateBuilder().WithoutTouchCapture().Build());
        var captured = 0;
        runtime.TouchCaptureHub.TouchCaptured += (_, _) => captured++;

        runtime.EnableTouchCapture();
        runtime.TouchCaptureHub.Record(CreateTouch());

        Assert.False(runtime.IsTouchCaptureEnabled);
        Assert.Equal(0, captured);
    }

    private static void AssertDefaultTouchCapture(TouchCaptureOptions? touchCapture)
    {
        Assert.NotNull(touchCapture);
        Assert.True(touchCapture.CaptureMoveEvents);
        Assert.True(touchCapture.CaptureCancelEvents);
        Assert.Equal(TouchCaptureOptions.DefaultMoveCaptureDistanceThreshold, touchCapture.MoveCaptureDistanceThreshold);
        Assert.Equal(TouchCaptureOptions.DefaultMoveCaptureFramesPerSecond, touchCapture.MoveCaptureFramesPerSecond);
    }

    private static CapturedTouch CreateTouch()
    {
        return new CapturedTouch(
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
            DateTimeOffset.UtcNow);
    }
}
