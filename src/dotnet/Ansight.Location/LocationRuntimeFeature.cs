namespace Ansight.Location;

internal sealed class LocationRuntimeFeature : IRuntimeFeature
{
    internal const string FeatureId = "location";
    private readonly LocationCaptureOptions options;

    internal LocationRuntimeFeature(LocationCaptureOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Id => FeatureId;

    public void Initialize(IRuntime runtime)
    {
        LocationCapture.Initialize(new LocationRecorder(runtime, options));
    }
}
