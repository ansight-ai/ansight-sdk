namespace Ansight;

public sealed class PlatformHostConnectionConfigReader : IHostConnectionConfigReader
{
#if ANDROID
    private readonly Func<Android.App.Activity?> currentActivityProvider;

    public PlatformHostConnectionConfigReader()
        : this(AndroidPairingActivityTracker.GetCurrentActivity)
    {
    }

    public PlatformHostConnectionConfigReader(Func<Android.App.Activity?> currentActivityProvider)
    {
        this.currentActivityProvider = currentActivityProvider ?? throw new ArgumentNullException(nameof(currentActivityProvider));
    }
#else
    public PlatformHostConnectionConfigReader()
    {
    }
#endif

    public bool CanRead(HostConnectionRequestKind kind)
    {
        return kind == HostConnectionRequestKind.QrCode;
    }

    public Task<string?> ReadConfigPayloadAsync(
        HostConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();
        return request.Kind switch
        {
            HostConnectionRequestKind.QrCode => ReadFromQrCodeAsync(request, cancellationToken),
            _ => Task.FromResult<string?>(null)
        };
    }

    private Task<string?> ReadFromQrCodeAsync(
        HostConnectionRequest request,
        CancellationToken cancellationToken)
    {
#if ANDROID
        return AndroidPlatformHostConnectionConfigReader.ReadFromQrCodeAsync(
            currentActivityProvider,
            request,
            cancellationToken);
#elif IOS || MACCATALYST
        return ApplePlatformHostConnectionConfigReader.ReadFromQrCodeAsync(request, cancellationToken);
#else
        throw new PlatformNotSupportedException("Ansight.Pairing is not supported on this platform.");
#endif
    }
}
