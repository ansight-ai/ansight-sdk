namespace Ansight.Pairing;

public sealed class PairingSessionClientBuilder
{
    private IPairingHostDiscoveryStrategy? _hostDiscoveryStrategy;
    private IDeviceAppProfileProvider? _deviceAppProfileProvider;

    public PairingSessionClientBuilder UseHostDiscoveryStrategy(IPairingHostDiscoveryStrategy? hostDiscoveryStrategy)
    {
        _hostDiscoveryStrategy = hostDiscoveryStrategy;
        return this;
    }

    public PairingSessionClientBuilder UseDeviceAppProfileProvider(IDeviceAppProfileProvider? deviceAppProfileProvider)
    {
        _deviceAppProfileProvider = deviceAppProfileProvider;
        return this;
    }

    public PairingSessionClient Build()
    {
        return new PairingSessionClient(_hostDiscoveryStrategy, _deviceAppProfileProvider);
    }
}
