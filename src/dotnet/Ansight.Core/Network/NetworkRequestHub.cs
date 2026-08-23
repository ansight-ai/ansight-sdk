namespace Ansight.Network;

internal sealed class NetworkRequestHub
{
    public event EventHandler<NetworkRequestCapturedEventArgs>? RequestCaptured;

    public void Record(NetworkRequestRecord request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequestCaptured?.Invoke(this, new NetworkRequestCapturedEventArgs(request));
    }
}
