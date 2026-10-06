namespace Ansight.Annotations;

using System.Reflection;
using System.Runtime.CompilerServices;

/// <summary>
/// Registers or customizes annotated feedback capture on the core runtime builder.
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
