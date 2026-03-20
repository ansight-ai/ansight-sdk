namespace Ansight.Pairing;

public sealed class PairingSessionClientBuilder
{
    private IDeviceAppProfileProvider? deviceAppProfileProvider;

    public PairingSessionClientBuilder UseDeviceAppProfileProvider(IDeviceAppProfileProvider? deviceAppProfileProvider)
    {
        this.deviceAppProfileProvider = deviceAppProfileProvider;
        return this;
    }

    public PairingSessionClient Build()
    {
        return new PairingSessionClient(deviceAppProfileProvider);
    }
}
