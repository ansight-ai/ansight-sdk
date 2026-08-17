namespace Ansight.Location;

/// <summary>Registers the optional observed-location module with an Ansight runtime.</summary>
public static class LocationOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithObservedLocationCapture(
        this Options.OptionsBuilder builder,
        Action<LocationCaptureOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var locationBuilder = new LocationCaptureOptionsBuilder();
        configure?.Invoke(locationBuilder);
        return builder.AddRuntimeFeature(new LocationRuntimeFeature(locationBuilder.Build()));
    }
}
