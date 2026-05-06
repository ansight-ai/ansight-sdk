namespace Ansight.Pairing;

/// <summary>
/// Fluent builder for creating a <see cref="PairingSessionClient"/> with customized profile collection behavior.
/// </summary>
public sealed class PairingSessionClientBuilder
{
    private IDeviceAppProfileProvider? deviceAppProfileProvider;
    private TimeSpan? cachedProfileRetention;

    /// <summary>
    /// Replaces the automatic baseline device/app profile collector used when opening a session.
    /// </summary>
    /// <param name="deviceAppProfileProvider">Custom provider used to create the baseline device app profile, or <see langword="null"/> to use the default collector.</param>
    /// <returns>The current builder.</returns>
    public PairingSessionClientBuilder UseDeviceAppProfileProvider(IDeviceAppProfileProvider? deviceAppProfileProvider)
    {
        this.deviceAppProfileProvider = deviceAppProfileProvider;
        return this;
    }

    /// <summary>
    /// Configures how long successful cached host connection profiles are retained.
    /// </summary>
    /// <param name="retention">Positive retention window for cached host profiles.</param>
    /// <returns>The current builder.</returns>
    public PairingSessionClientBuilder UseCachedProfileRetention(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "Cached profile retention must be positive.");
        }

        cachedProfileRetention = retention;
        return this;
    }

    /// <summary>
    /// Builds a new <see cref="PairingSessionClient"/> instance.
    /// </summary>
    /// <returns>A configured pairing session client.</returns>
    public PairingSessionClient Build()
    {
        return cachedProfileRetention is null
            ? new PairingSessionClient(deviceAppProfileProvider)
            : new PairingSessionClient(deviceAppProfileProvider, cachedProfileRetention.Value);
    }
}
