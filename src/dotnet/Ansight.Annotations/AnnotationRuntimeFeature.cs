namespace Ansight.Annotations;

using System.Reflection;

internal sealed class AnnotationRuntimeFeature : IRuntimeFeature
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

    public void Initialize(IRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!AnnotationBuildPolicy.IsDebugBuild(registrationAssembly))
        {
            Feedback.InitializeDisabled("Annotation capture is available only in Debug application builds.");
            return;
        }

        Feedback.Initialize(new AnnotationService(runtime, options));
    }
}
