#if ANDROID
namespace Ansight.Tools.JniReferenceDiagnostics;

/// <summary>
/// Registers Android JNI reference diagnostics with the Ansight tool registry.
/// </summary>
public static class JniReferenceDiagnosticsOptionsBuilderExtensions
{
    /// <summary>
    /// Registers the bounded JNI object-reference graph capture tool.
    /// </summary>
    public static Options.OptionsBuilder WithJniReferenceDiagnosticsTools(
        this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddTool(new CaptureJniReferenceGraphTool());
    }
}
#endif
