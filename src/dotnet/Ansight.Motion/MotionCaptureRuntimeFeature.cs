namespace Ansight.Motion;

using System.Text.Json.Nodes;
using Ansight.Native;

internal sealed class MotionCaptureRuntimeFeature : IRuntimeFeature, INativeRuntimeOptionsContributor
{
    private readonly MotionCaptureOptions options;

    public MotionCaptureRuntimeFeature(MotionCaptureOptions options) => this.options = options;

    public string Id => "motion";

    public void Initialize(IRuntime runtime) => MotionCapture.Initialize(runtime, options);

    public void ContributeNativeOptions(JsonObject nativeOptions)
    {
        nativeOptions["motionCapture"] = new JsonObject
        {
            ["captureShake"] = options.CaptureShake,
            ["captureAccelerometer"] = options.CaptureAccelerometer,
            ["minimumSampleIntervalMilliseconds"] = options.MinimumSampleIntervalMilliseconds
        };
    }
}
