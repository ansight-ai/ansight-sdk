#if ANDROID
using Android.App;
using Android.Gms.Common.ModuleInstall;
using Android.Gms.Tasks;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;
using CancellationToken = System.Threading.CancellationToken;

namespace Ansight;

internal static class AndroidPlatformHostConnectionConfigReader
{
    private const string LogTag = "AnsightPairing";

    public static Task<string?> ReadFromQrCodeAsync(
        Func<Activity?> currentActivityProvider,
        HostConnectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentActivityProvider);

        LogInfo(
            $"QR code read requested. kind={request.Kind}, title={DescribeNullable(request.Title)}, source={DescribeNullable(request.SourceDescription)}.");

        if (cancellationToken.IsCancellationRequested)
        {
            LogWarning("QR code read request was canceled before activity resolution.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Activity? activity;
        try
        {
            activity = currentActivityProvider();
        }
        catch (Exception ex)
        {
            LogException("QR code read failed while resolving the current Android activity.", ex);
            throw;
        }

        if (activity is null)
        {
            return CreateFailureTask("QR pairing is unavailable because no current Android activity was provided.");
        }

        LogInfo($"Current Android activity resolved. {DescribeActivity(activity)}.");

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
        LogInfo("Creating Google ML Kit code scanner for QR-only scan.");

        using var scanner = CreateScanner(activity);
        LogInfo("Google ML Kit code scanner created.");

        await EnsureScannerModuleAvailableAsync(activity, scanner, cancellationToken);

        var barcode = await StartScanAsync(scanner, activity, cancellationToken);
        if (barcode is null)
        {
            LogWarning("QR scanner completed without a barcode. The scan was likely canceled by the user or scanner UI.");
            return null;
        }

        var payload = barcode?.RawValue?.Trim();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            LogInfo($"QR scanner returned raw payload. {DescribePayload(payload)}.");
            return payload;
        }

        LogWarning("QR scanner barcode did not include RawValue. Falling back to DisplayValue.");

        payload = barcode?.DisplayValue?.Trim();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            LogInfo($"QR scanner returned display payload. {DescribePayload(payload)}.");
            return payload;
        }

        LogWarning("QR scanner returned a barcode but both RawValue and DisplayValue were empty.");
        throw new InvalidOperationException("The scanned QR code did not contain a pairing payload.");
    }

    private static Task<string?> CreateFailureTask(string message)
    {
        LogWarning(message);
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

    private static async System.Threading.Tasks.Task EnsureScannerModuleAvailableAsync(
        Activity activity,
        IGmsBarcodeScanner scanner,
        CancellationToken cancellationToken)
    {
        try
        {
            LogInfo("Checking Google Play Services QR scanner module availability.");

            using var moduleInstallClient = ModuleInstall.GetClient(activity);
            using var listener = new ScannerModuleInstallListener();
            using var request = ModuleInstallRequest.NewBuilder()
                .AddApi(scanner)
                .SetListener(listener)
                .Build();

            LogInfo("Requesting Google Play Services QR scanner module installation/availability.");

            var response = await AwaitJavaTaskAsync<ModuleInstallResponse>(
                moduleInstallClient.InstallModules(request),
                activity,
                cancellationToken,
                "QR scanner module installation failed",
                "QR scanner module install request");

            LogInfo($"QR scanner module install response received. alreadyInstalled={response?.AreModulesAlreadyInstalled().ToString() ?? "Unknown"}.");

            if (response?.AreModulesAlreadyInstalled() == true)
            {
                LogInfo("QR scanner module is already installed.");
                return;
            }

            LogInfo("Waiting for QR scanner module install listener to report completion.");
            await listener.WaitForCompletionAsync(cancellationToken);
            LogInfo("QR scanner module install listener reported completion.");
        }
        catch (OperationCanceledException ex)
        {
            LogException("QR scanner module availability check was canceled.", ex);
            throw;
        }
        catch (Exception ex)
        {
            LogException("QR scanner module availability check failed.", ex);
            throw;
        }
    }

    private static Task<Barcode?> StartScanAsync(
        IGmsBarcodeScanner scanner,
        Activity activity,
        CancellationToken cancellationToken)
    {
        LogInfo(
            $"Preparing QR scanner UI launch. currentThreadIsMain={IsCurrentThreadMain()}, activity={DescribeActivity(activity)}.");

        var completionSource = new TaskCompletionSource<Barcode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(() =>
        {
            LogWarning("QR scanner task was canceled by the provided cancellation token.");
            completionSource.TrySetCanceled(cancellationToken);
        });
        _ = completionSource.Task.ContinueWith(
            _ => cancellationRegistration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        LogInfo("Posting QR scanner launch to the Android UI thread.");

        activity.RunOnUiThread(() =>
        {
            try
            {
                LogInfo($"QR scanner UI-thread launch callback entered. currentThreadIsMain={IsCurrentThreadMain()}.");

                if (completionSource.Task.IsCompleted)
                {
                    LogWarning("QR scanner launch skipped because the completion task was already completed.");
                    return;
                }

                LogInfo("Calling Google ML Kit scanner.StartScan().");
                var scanTask = scanner.StartScan();
                LogInfo("Google ML Kit scanner.StartScan() returned a task. Attaching listeners.");

                scanTask.AddOnSuccessListener(activity, new BarcodeSuccessListener(completionSource));
                scanTask.AddOnFailureListener(activity, new BarcodeFailureListener(completionSource));
                scanTask.AddOnCanceledListener(activity, new BarcodeCanceledListener(completionSource));
                LogInfo("QR scanner task listeners attached.");
            }
            catch (Exception ex)
            {
                LogException("QR scanner launch threw before listeners could complete the scan task.", ex);
                completionSource.TrySetException(new InvalidOperationException($"QR scan failed: {ex.Message}", ex));
            }
        });

        return completionSource.Task;
    }

    private static Task<T?> AwaitJavaTaskAsync<T>(
        Android.Gms.Tasks.Task task,
        Activity activity,
        CancellationToken cancellationToken,
        string failurePrefix,
        string operationName)
        where T : class
    {
        var completionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(() =>
        {
            LogWarning($"{operationName} was canceled by the provided cancellation token.");
            completionSource.TrySetCanceled(cancellationToken);
        });
        _ = completionSource.Task.ContinueWith(
            _ => cancellationRegistration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        LogInfo($"Attaching Google Play Services task listeners. operation={operationName}.");

        task.AddOnSuccessListener(activity, new JavaTaskSuccessListener<T>(completionSource, operationName));
        task.AddOnFailureListener(activity, new JavaTaskFailureListener<T>(completionSource, failurePrefix, operationName));
        task.AddOnCanceledListener(activity, new JavaTaskCanceledListener<T>(completionSource, operationName));

        return completionSource.Task;
    }

    private static void LogInfo(string message)
    {
        var formatted = $"[Android QR pairing] {message}";
        Logger.Info(formatted);
        global::Android.Util.Log.Info(LogTag, formatted);
    }

    private static void LogWarning(string message)
    {
        var formatted = $"[Android QR pairing] {message}";
        Logger.Warning(formatted);
        global::Android.Util.Log.Warn(LogTag, formatted);
    }

    private static void LogException(string message, Exception exception)
    {
        var formatted = $"[Android QR pairing] {message} {exception}";
        Logger.Warning(formatted);
        Logger.Exception(exception);
        global::Android.Util.Log.Warn(LogTag, formatted);
    }

    private static string DescribeActivity(Activity activity)
    {
        var activityType = activity.GetType().FullName ?? activity.GetType().Name;
        return $"type={activityType}, package={DescribeNullable(activity.PackageName)}, finishing={activity.IsFinishing}, destroyed={activity.IsDestroyed}";
    }

    private static string DescribePayload(string payload)
    {
        return $"length={payload.Length}, startsWithJson={payload.StartsWith('{')}, startsWithHttp={payload.StartsWith("http", StringComparison.OrdinalIgnoreCase)}";
    }

    private static string DescribeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
    }

    private static bool IsCurrentThreadMain()
    {
        return global::Android.OS.Looper.MyLooper() == global::Android.OS.Looper.MainLooper;
    }

    private sealed class BarcodeSuccessListener(TaskCompletionSource<Barcode?> completionSource)
        : Java.Lang.Object, IOnSuccessListener
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            if (result is not Barcode barcode)
            {
                LogWarning($"QR scanner success listener received unexpected result type {result?.GetType().FullName ?? "<null>"}.");
                completionSource.TrySetResult(null);
                return;
            }

            LogInfo(
                $"QR scanner success listener received barcode. rawLength={barcode.RawValue?.Length.ToString() ?? "<null>"}, displayLength={barcode.DisplayValue?.Length.ToString() ?? "<null>"}.");
            completionSource.TrySetResult(barcode);
        }
    }

    private sealed class JavaTaskSuccessListener<T>(
        TaskCompletionSource<T?> completionSource,
        string operationName)
        : Java.Lang.Object, IOnSuccessListener
        where T : class
    {
        public void OnSuccess(Java.Lang.Object? result)
        {
            LogInfo($"{operationName} succeeded. resultType={result?.GetType().FullName ?? "<null>"}.");
            completionSource.TrySetResult(result as T);
        }
    }

    private sealed class JavaTaskFailureListener<T>(
        TaskCompletionSource<T?> completionSource,
        string failurePrefix,
        string operationName)
        : Java.Lang.Object, IOnFailureListener
        where T : class
    {
        public void OnFailure(Java.Lang.Exception ex)
        {
            var message = ex.Message;
            LogWarning($"{operationName} failed. exceptionType={ex.GetType().FullName}, message={DescribeNullable(message)}.");
            completionSource.TrySetException(new InvalidOperationException(
                string.IsNullOrWhiteSpace(message) ? $"{failurePrefix}." : $"{failurePrefix}: {message}",
                ex));
        }
    }

    private sealed class JavaTaskCanceledListener<T>(
        TaskCompletionSource<T?> completionSource,
        string operationName)
        : Java.Lang.Object, IOnCanceledListener
        where T : class
    {
        public void OnCanceled()
        {
            LogWarning($"{operationName} was canceled by Google Play Services.");
            completionSource.TrySetCanceled();
        }
    }

    private sealed class BarcodeFailureListener(TaskCompletionSource<Barcode?> completionSource)
        : Java.Lang.Object, IOnFailureListener
    {
        public void OnFailure(Java.Lang.Exception ex)
        {
            LogWarning($"QR scanner failure listener invoked. exceptionType={ex.GetType().FullName}, message={DescribeNullable(ex.Message)}.");
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
            LogWarning("QR scanner canceled listener invoked.");
            completionSource.TrySetResult(null);
        }
    }

    private sealed class ScannerModuleInstallListener : Java.Lang.Object, IInstallStatusListener
    {
        private readonly TaskCompletionSource<bool> completionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnInstallStatusUpdated(ModuleInstallStatusUpdate update)
        {
            var installState = update.InstallStateCode();
            LogInfo($"QR scanner module install status update. state={installState}, errorCode={update.ErrorCode}.");

            switch (installState)
            {
                case ModuleInstallStatusUpdate.InstallState.StateCompleted:
                    LogInfo("QR scanner module install completed.");
                    completionSource.TrySetResult(true);
                    break;
                case ModuleInstallStatusUpdate.InstallState.StateCanceled:
                    LogWarning("QR scanner module install was canceled.");
                    completionSource.TrySetCanceled();
                    break;
                case ModuleInstallStatusUpdate.InstallState.StateFailed:
                    LogWarning($"QR scanner module install failed with status code {update.ErrorCode}.");
                    completionSource.TrySetException(new InvalidOperationException(
                        $"QR scanner module installation failed with status code {update.ErrorCode}."));
                    break;
                default:
                    LogInfo($"QR scanner module install is still in progress. state={installState}.");
                    break;
            }
        }

        public System.Threading.Tasks.Task WaitForCompletionAsync(CancellationToken cancellationToken)
        {
            return completionSource.Task.WaitAsync(cancellationToken);
        }
    }
}
#endif
