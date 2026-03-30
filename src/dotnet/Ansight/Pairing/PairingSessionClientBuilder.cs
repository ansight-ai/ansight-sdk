namespace Ansight.Pairing;

/// <summary>
/// Fluent builder for creating a <see cref="PairingSessionClient"/> with customized profile collection behavior.
/// </summary>
public sealed class PairingSessionClientBuilder
{
    private IDeviceAppProfileProvider? deviceAppProfileProvider;

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
    /// Builds a new <see cref="PairingSessionClient"/> instance.
    /// </summary>
    /// <returns>A configured pairing session client.</returns>
    public PairingSessionClient Build()
    {
        return new PairingSessionClient(deviceAppProfileProvider);
    }
}
