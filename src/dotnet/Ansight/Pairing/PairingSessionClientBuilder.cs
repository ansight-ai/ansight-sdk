namespace Ansight.Pairing;

public sealed class PairingSessionClientBuilder
{
    private IDeviceAppProfileProvider? _deviceAppProfileProvider;

    public PairingSessionClientBuilder UseDeviceAppProfileProvider(IDeviceAppProfileProvider? deviceAppProfileProvider)
    {
        _deviceAppProfileProvider = deviceAppProfileProvider;
        return this;
    }

    public PairingSessionClient Build()
    {
        return new PairingSessionClient(_deviceAppProfileProvider);
    }
}
