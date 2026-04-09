#if ANDROID
using Android.App;
using Android.Gms.Tasks;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;
using CancellationToken = System.Threading.CancellationToken;

namespace Ansight;

internal static class AndroidPlatformHostConnectionConfigReader
{
    public static Task<string?> ReadFromQrCodeAsync(
        Func<Activity?> currentActivityProvider,
        HostConnectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentActivityProvider);

        cancellationToken.ThrowIfCancellationRequested();

        var activity = currentActivityProvider();
        if (activity is null)
        {
            return CreateFailureTask("QR pairing is unavailable because no current Android activity was provided.");
        }

        if (activity.IsFinishing || activity.IsDestroyed)
        {
            return CreateFailureTask("QR pairing is unavailable because the current Android activity is no longer active.");
        }

        return ReadFromScannerAsync(activity, cancellationToken);
    }

    private static async Task<string?> ReadFromScannerAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        using var scanner = CreateScanner(activity);

        var barcode = await StartScanAsync(scanner, activity, cancellationToken);
        if (barcode is null)
        {
            return null;
        }

        var payload = barcode?.RawValue?.Trim();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            return payload;
        }

        payload = barcode?.DisplayValue?.Trim();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            return payload;
        }

        throw new InvalidOperationException("The scanned QR code did not contain a pairing payload.");
    }

    private static Task<string?> CreateFailureTask(string message)
    {
        return System.Threading.Tasks.Task.FromException<string?>(new InvalidOperationException(message));
    }

    private static IGmsBarcodeScanner CreateScanner(Activity activity)
    {
        var options = new GmsBarcodeScannerOptions.Builder()
            .SetBarcodeFormats(Barcode.FormatQrCode, Array.Empty<int>())
            .EnableAutoZoom()
            .Build();

        return GmsBarcodeScanning.GetClient(activity, options);
    }

    private static Task<Barcode?> StartScanAsync(
        IGmsBarcodeScanner scanner,
        Activity activity,
        CancellationToken cancellationToken)
    {
        var completionSource = new TaskCompletionSource<Barcode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(static state =>
        {
            var source = (TaskCompletionSource<Barcode?>)state!;
            source.TrySetCanceled();
        }, completionSource);
        _ = completionSource.Task.ContinueWith(
            _ => cancellationRegistration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        activity.RunOnUiThread(() =>
        {
            try
            {
                if (completionSource.Task.IsCompleted)
                {
                    return;
                }

                var scanTask = scanner.StartScan();

                scanTask.AddOnSuccessListener(activity, new BarcodeSuccessListener(completionSource));
                scanTask.AddOnFailureListener(activity, new BarcodeFailureListener(completionSource));
                scanTask.AddOnCanceledListener(activity, new BarcodeCanceledListener(completionSource));
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(new InvalidOperationException($"QR scan failed: {ex.Message}", ex));
            }
        });

        return completionSource.Task;
    }

    private sealed class BarcodeSuccessListener(TaskCompletionSource<Barcode?> completionSource)
        : Java.Lang.Object, IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            completionSource.TrySetResult(result as Barcode);
        }
    }

    private sealed class BarcodeFailureListener(TaskCompletionSource<Barcode?> completionSource)
        : Java.Lang.Object, IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception ex)
        {
            completionSource.TrySetException(new InvalidOperationException(CreateFailureMessage(ex), ex));
        }

        private static string CreateFailureMessage(Java.Lang.Exception exception)
        {
            var message = exception.Message;
            if (string.IsNullOrWhiteSpace(message))
            {
                return "QR scan failed.";
            }

            if (message.Contains("permission", StringComparison.OrdinalIgnoreCase)
                || (message.Contains("camera", StringComparison.OrdinalIgnoreCase)
                    && message.Contains("denied", StringComparison.OrdinalIgnoreCase)))
            {
                return "QR pairing requires camera access. Grant camera permission before scanning.";
            }

            return $"QR scan failed: {message}";
        }
    }

    private sealed class BarcodeCanceledListener(TaskCompletionSource<Barcode?> completionSource)
        : Java.Lang.Object, IOnCanceledListener
    {
        public void OnCanceled()
        {
            completionSource.TrySetResult(null);
        }
    }
}
#endif
