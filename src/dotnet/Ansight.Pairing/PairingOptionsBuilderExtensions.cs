namespace Ansight;

public static class PairingOptionsBuilderExtensions
{
#if ANDROID
    public static Options.OptionsBuilder WithPlatformPairing(
        this Options.OptionsBuilder builder,
        Func<Android.App.Activity?> currentActivityProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(currentActivityProvider);

        return builder.ConfigureHostConnection(hostConnection =>
            hostConnection.UseConfigReader(new PlatformHostConnectionConfigReader(currentActivityProvider)));
    }
#else
    public static Options.OptionsBuilder WithPlatformPairing(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureHostConnection(hostConnection =>
            hostConnection.UseConfigReader(new PlatformHostConnectionConfigReader()));
    }
#endif
}
