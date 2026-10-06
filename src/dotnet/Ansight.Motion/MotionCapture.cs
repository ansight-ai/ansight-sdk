namespace Ansight.Motion;

using System.Text.Json;
using Ansight.Telemetry.Events;

/// <summary>Forwards motion already observed by the app into the Ansight timeline.</summary>
public static class MotionCapture
{
    private static readonly object gate = new();
    private static IRuntime? runtime;
    private static MotionCaptureOptions? options;
    private static long lastSampleTick;

    internal static void Initialize(IRuntime initializedRuntime, MotionCaptureOptions captureOptions)
    {
        lock (gate)
        {
            runtime = initializedRuntime;
            options = captureOptions;
            lastSampleTick = 0;
        }
    }

    public static void RecordShake(string source = "app")
    {
        IRuntime? target;
        lock (gate)
        {
            target = options?.CaptureShake == true && runtime?.IsActive == true ? runtime : null;
        }
        target?.Event("motion.shake", AppEventType.Motion,
            JsonSerializer.Serialize(new { source }));
    }

    /// <summary>Records one app-observed acceleration sample in m/s².</summary>
    public static void RecordAccelerometer(double x, double y, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return;
        IRuntime? target;
        lock (gate)
        {
            target = options?.CaptureAccelerometer == true && runtime?.IsActive == true ? runtime : null;
            if (target is null) return;
            var now = Environment.TickCount64;
            if (now - lastSampleTick < options!.MinimumSampleIntervalMilliseconds) return;
            lastSampleTick = now;
        }
        target.Event("motion.accelerometer", AppEventType.Motion,
            JsonSerializer.Serialize(new { x, y, z, unit = "m/s2", source = "app" }));
    }
}
