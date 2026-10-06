namespace Ansight.Motion;

/// <summary>Enables motion capture in the core runtime.</summary>
public static class MotionCaptureOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithMotionCapture(
        this Options.OptionsBuilder builder,
        Action<MotionCaptureOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new MotionCaptureOptions();
        configure?.Invoke(options);
        return builder.AddRuntimeFeature(new MotionCaptureRuntimeFeature(options.Validated()));
    }
}
