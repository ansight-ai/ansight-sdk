#if ANDROID
using Android.App;

namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
    private static readonly Android.OS.Handler MainHandler = new(Android.OS.Looper.MainLooper!);

    private static partial Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync<ISessionJpegCaptureSurface?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TryCaptureSurface(options, out var surface) ? surface : null;
        });
    }

    private static partial SessionJpegFrame? EncodeSurfaceCore(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options)
    {
        return surface is SessionJpegCaptureSurface androidSurface
            ? EncodeSurface(androidSurface, options)
            : null;
    }

    private static Task<T?> InvokeOnUiThreadAsync<T>(Func<T?> capture)
    {
        var taskCompletionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainHandler.Post(() =>
        {
            try
            {
                taskCompletionSource.SetResult(capture());
            }
            catch (Exception ex)
            {
                taskCompletionSource.SetException(ex);
            }
        });

        return taskCompletionSource.Task;
    }

    private static bool TryCaptureSurface(SessionJpegCaptureOptions options, out SessionJpegCaptureSurface? surface)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null || rootView.Width <= 0 || rootView.Height <= 0)
        {
            surface = null;
            return false;
        }

        var targetWidth = ResolveTargetWidth(rootView.Width, options.MaxWidth);
        var targetHeight = ResolveScaledHeight(rootView.Width, rootView.Height, targetWidth);
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            surface = null;
            return false;
        }

        var bitmap = Android.Graphics.Bitmap.CreateBitmap(targetWidth, targetHeight, Android.Graphics.Bitmap.Config.Argb8888!);
        using (var canvas = new Android.Graphics.Canvas(bitmap))
        {
            if (targetWidth != rootView.Width || targetHeight != rootView.Height)
            {
                canvas.Scale(targetWidth / (float)rootView.Width, targetHeight / (float)rootView.Height);
            }

            rootView.Draw(canvas);
        }

        surface = new SessionJpegCaptureSurface(bitmap, DateTimeOffset.UtcNow, targetWidth, targetHeight);
        return true;
    }

    private static SessionJpegFrame? EncodeSurface(SessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
    {
        using var stream = new PooledBufferStream(EstimateInitialPayloadCapacity(surface.Width, surface.Height));
        stream.ReservePrefix(SessionJpegWireProtocol.HeaderSize);
        if (!surface.Bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, options.Quality, stream))
        {
            return null;
        }

        var jpegLength = stream.LengthWritten - SessionJpegWireProtocol.HeaderSize;
        SessionJpegWireProtocol.WriteHeader(
            stream.GetWrittenSpan(SessionJpegWireProtocol.HeaderSize),
            surface.CapturedAtUtc,
            surface.Width,
            surface.Height,
            options.Quality,
            jpegLength);

        return stream.DetachFrame();
    }

    private sealed class SessionJpegCaptureSurface : ISessionJpegCaptureSurface
    {
        public SessionJpegCaptureSurface(Android.Graphics.Bitmap bitmap, DateTimeOffset capturedAtUtc, int width, int height)
        {
            Bitmap = bitmap;
            CapturedAtUtc = capturedAtUtc;
            Width = width;
            Height = height;
        }

        public Android.Graphics.Bitmap Bitmap { get; }

        public DateTimeOffset CapturedAtUtc { get; }

        public int Width { get; }

        public int Height { get; }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }

    private sealed class AndroidActivityTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
    {
        private static readonly object Sync = new();
        private static AndroidActivityTracker? instance;
        private Activity? currentActivity;

        internal static Activity? GetCurrentActivity()
        {
            EnsureRegistered();
            lock (Sync)
            {
                return instance?.currentActivity;
            }
        }

        private static void EnsureRegistered()
        {
            if (instance is not null)
            {
                return;
            }

            lock (Sync)
            {
                if (instance is not null)
                {
                    return;
                }

                if (Application.Context is not Application application)
                {
                    return;
                }

                instance = new AndroidActivityTracker();
                application.RegisterActivityLifecycleCallbacks(instance);
            }
        }

        public void OnActivityCreated(Activity activity, Android.OS.Bundle? savedInstanceState)
        {
            lock (Sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivityDestroyed(Activity activity)
        {
            lock (Sync)
            {
                if (ReferenceEquals(currentActivity, activity))
                {
                    currentActivity = null;
                }
            }
        }

        public void OnActivityPaused(Activity activity)
        {
        }

        public void OnActivityResumed(Activity activity)
        {
            lock (Sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivitySaveInstanceState(Activity activity, Android.OS.Bundle outState)
        {
        }

        public void OnActivityStarted(Activity activity)
        {
            lock (Sync)
            {
                currentActivity = activity;
            }
        }

        public void OnActivityStopped(Activity activity)
        {
        }
    }
}
#endif
