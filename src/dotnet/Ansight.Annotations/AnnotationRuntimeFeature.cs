namespace Ansight.Annotations;

using System.Reflection;
using System.Text.Json.Nodes;
using Ansight.Native;

internal sealed class AnnotationRuntimeFeature : IRuntimeFeature, INativeRuntimeOptionsContributor
{
    internal const string FeatureId = "annotations";

    private readonly AnnotationOptions options;
    private readonly Assembly registrationAssembly;

    internal AnnotationRuntimeFeature(AnnotationOptions options, Assembly registrationAssembly)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.registrationAssembly = registrationAssembly ?? throw new ArgumentNullException(nameof(registrationAssembly));
    }

    public string Id => FeatureId;

    public void ContributeNativeOptions(JsonObject nativeOptions)
    {
        nativeOptions["annotatedFeedback"] = new JsonObject
        {
            ["enabled"] = true,
            ["debugBuildOverride"] = AnnotationBuildPolicy.IsDebugBuild(registrationAssembly),
            ["captureScreenshot"] = options.CaptureScreenshot,
            ["captureVisualTrees"] = options.CaptureVisualTrees,
            ["screenshotQuality"] = options.ScreenshotQuality,
            ["screenshotMaxWidth"] = options.ScreenshotMaxWidth,
            ["visualTreeMaxDepth"] = options.VisualTreeMaxDepth,
            ["visualTreeMaxNodes"] = options.VisualTreeMaxNodes
        };
    }

    public void Initialize(IRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!AnnotationBuildPolicy.IsDebugBuild(registrationAssembly))
        {
            Annotate.InitializeDisabled("Annotation capture is available only in Debug application builds.");
            return;
        }

        Annotate.Initialize(new AnnotationService(runtime, options));
    }
}
