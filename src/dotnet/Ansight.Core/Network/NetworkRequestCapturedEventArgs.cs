namespace Ansight.Network;

internal sealed class NetworkRequestCapturedEventArgs : EventArgs
{
    public NetworkRequestCapturedEventArgs(NetworkRequestRecord request)
    {
        Request = request;
    }

    public NetworkRequestRecord Request { get; }
}
