namespace Ansight.Pairing;

public sealed class PairingSessionClientBuilder
{
    private IPairingHostDiscoveryStrategy? _hostDiscoveryStrategy;

    public PairingSessionClientBuilder UseHostDiscoveryStrategy(IPairingHostDiscoveryStrategy? hostDiscoveryStrategy)
    {
        _hostDiscoveryStrategy = hostDiscoveryStrategy;
        return this;
    }

    public PairingSessionClient Build()
    {
        return new PairingSessionClient(_hostDiscoveryStrategy);
    }
}
