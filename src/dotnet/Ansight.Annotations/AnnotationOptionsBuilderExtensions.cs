namespace Ansight.Annotations;

using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>
/// Registers opt-in annotated feedback capture with the Ansight runtime.
/// </summary>
public static class AnnotatedFeedbackOptionsBuilderExtensions
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Options.OptionsBuilder WithAnnotatedFeedback(
        this Options.OptionsBuilder builder,
        Action<AnnotationOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var annotationBuilder = new AnnotationOptionsBuilder();
        configure?.Invoke(annotationBuilder);
        var feature = new AnnotationRuntimeFeature(annotationBuilder.Build(), Assembly.GetCallingAssembly());
        return builder.AddRuntimeFeature(feature);
    }
}
