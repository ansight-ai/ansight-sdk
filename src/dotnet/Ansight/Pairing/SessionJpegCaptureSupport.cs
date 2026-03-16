namespace Ansight.Pairing;

internal static class SessionJpegCaptureSupport
{
    public static async Task<SessionJpegFrame?> CaptureAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var surface = await CaptureSurfaceAsync(options, cancellationToken);
        if (surface is null)
        {
            return null;
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return EncodeSurface(surface, options);
            }, cancellationToken);
        }
        finally
        {
            surface.Dispose();
        }
    }

#if ANDROID
    private static readonly Android.OS.Handler MainHandler = new(Android.OS.Looper.MainLooper);

    private static Task<SessionJpegCaptureSurface?> CaptureSurfaceAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TryCaptureSurface(out var surface) ? surface : null;
        });
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
#elif IOS || MACCATALYST
    private static Task<SessionJpegCaptureSurface?> CaptureSurfaceAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TryCaptureSurface(options, out var surface) ? surface : null;
        });
    }

    private static Task<T?> InvokeOnUiThreadAsync<T>(Func<T?> capture)
    {
        var taskCompletionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIKit.UIApplication.SharedApplication.InvokeOnMainThread(() =>
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
#else
    private static Task<SessionJpegCaptureSurface?> CaptureSurfaceAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
        => Task.FromResult<SessionJpegCaptureSurface?>(null);
#endif

#if ANDROID
    private static bool TryCaptureSurface(out SessionJpegCaptureSurface? surface)
    {
        var activity = AndroidActivityTracker.GetCurrentActivity();
        var rootView = activity?.Window?.DecorView?.RootView;
        if (rootView == null || rootView.Width <= 0 || rootView.Height <= 0)
        {
            surface = null;
            return false;
        }

        var bitmap = Android.Graphics.Bitmap.CreateBitmap(rootView.Width, rootView.Height, Android.Graphics.Bitmap.Config.Argb8888!);
        using (var canvas = new Android.Graphics.Canvas(bitmap))
        {
            rootView.Draw(canvas);
        }

        surface = new SessionJpegCaptureSurface(bitmap, DateTimeOffset.UtcNow);
        return true;
    }

    private static SessionJpegFrame? EncodeSurface(SessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
    {
        Android.Graphics.Bitmap workingBitmap = surface.Bitmap;
        Android.Graphics.Bitmap? scaledBitmap = null;
        if (options.MaxWidth.HasValue && workingBitmap.Width > options.MaxWidth.Value)
        {
            var scaledHeight = (int)Math.Round(workingBitmap.Height * (options.MaxWidth.Value / (double)workingBitmap.Width));
            scaledBitmap = Android.Graphics.Bitmap.CreateScaledBitmap(workingBitmap, options.MaxWidth.Value, scaledHeight, filter: true);
            workingBitmap = scaledBitmap;
        }

        try
        {
            using var stream = new MemoryStream();
            if (!workingBitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, options.Quality, stream))
            {
                return null;
            }

            return new SessionJpegFrame(
                CapturedAtUtc: surface.CapturedAtUtc,
                Width: workingBitmap.Width,
                Height: workingBitmap.Height,
                Quality: options.Quality,
                Bytes: stream.ToArray());
        }
        finally
        {
            scaledBitmap?.Dispose();
        }
    }

    private sealed class SessionJpegCaptureSurface : IDisposable
    {
        public SessionJpegCaptureSurface(Android.Graphics.Bitmap bitmap, DateTimeOffset capturedAtUtc)
        {
            Bitmap = bitmap;
            CapturedAtUtc = capturedAtUtc;
        }

        public Android.Graphics.Bitmap Bitmap { get; }

        public DateTimeOffset CapturedAtUtc { get; }

        public void Dispose()
        {
            Bitmap.Dispose();
        }
    }

    private sealed class AndroidActivityTracker : Java.Lang.Object, Android.App.Application.IActivityLifecycleCallbacks
    {
        private static readonly object Sync = new();
        private static AndroidActivityTracker? _instance;
        private Android.App.Activity? _currentActivity;

        internal static Android.App.Activity? GetCurrentActivity()
        {
            EnsureRegistered();
            lock (Sync)
            {
                return _instance?._currentActivity;
            }
        }

        private static void EnsureRegistered()
        {
            if (_instance is not null)
            {
                return;
            }

            lock (Sync)
            {
                if (_instance is not null)
                {
                    return;
                }

                if (Android.App.Application.Context is not Android.App.Application application)
                {
                    return;
                }

                _instance = new AndroidActivityTracker();
                application.RegisterActivityLifecycleCallbacks(_instance);
            }
        }

        public void OnActivityCreated(Android.App.Activity activity, Android.OS.Bundle? savedInstanceState)
        {
            lock (Sync)
            {
                _currentActivity = activity;
            }
        }

        public void OnActivityDestroyed(Android.App.Activity activity)
        {
            lock (Sync)
            {
                if (ReferenceEquals(_currentActivity, activity))
                {
                    _currentActivity = null;
                }
            }
        }

        public void OnActivityPaused(Android.App.Activity activity)
        {
        }

        public void OnActivityResumed(Android.App.Activity activity)
        {
            lock (Sync)
            {
                _currentActivity = activity;
            }
        }

        public void OnActivitySaveInstanceState(Android.App.Activity activity, Android.OS.Bundle outState)
        {
        }

        public void OnActivityStarted(Android.App.Activity activity)
        {
            lock (Sync)
            {
                _currentActivity = activity;
            }
        }

        public void OnActivityStopped(Android.App.Activity activity)
        {
        }
    }
#elif IOS || MACCATALYST
    private static bool TryCaptureSurface(SessionJpegCaptureOptions options, out SessionJpegCaptureSurface? surface)
    {
        var window = GetActiveWindow();
        if (window == null)
        {
            surface = null;
            return false;
        }

        var originalBounds = window.Bounds;
        if (originalBounds.Width <= 0 || originalBounds.Height <= 0)
        {
            surface = null;
            return false;
        }

        var targetSize = originalBounds.Size;
        if (options.MaxWidth.HasValue && targetSize.Width > options.MaxWidth.Value)
        {
            var scaleFactor = options.MaxWidth.Value / (double)targetSize.Width;
            targetSize = new CoreGraphics.CGSize(options.MaxWidth.Value, targetSize.Height * scaleFactor);
        }

        using var renderer = new UIKit.UIGraphicsImageRenderer(targetSize);
        var image = renderer.CreateImage(renderContext =>
        {
            renderContext.CGContext.ScaleCTM((nfloat)(targetSize.Width / originalBounds.Width), (nfloat)(targetSize.Height / originalBounds.Height));
            window.DrawViewHierarchy(originalBounds, afterScreenUpdates: true);
        });

        surface = new SessionJpegCaptureSurface(
            image,
            DateTimeOffset.UtcNow,
            (int)Math.Round(targetSize.Width),
            (int)Math.Round(targetSize.Height));
        return true;
    }

    private static SessionJpegFrame? EncodeSurface(SessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
    {
        using var imageData = surface.Image.AsJPEG((nfloat)(options.Quality / 100d));
        if (imageData is null)
        {
            return null;
        }

        return new SessionJpegFrame(
            CapturedAtUtc: surface.CapturedAtUtc,
            Width: surface.Width,
            Height: surface.Height,
            Quality: options.Quality,
            Bytes: imageData.ToArray());
    }

    private sealed class SessionJpegCaptureSurface : IDisposable
    {
        public SessionJpegCaptureSurface(UIKit.UIImage image, DateTimeOffset capturedAtUtc, int width, int height)
        {
            Image = image;
            CapturedAtUtc = capturedAtUtc;
            Width = width;
            Height = height;
        }

        public UIKit.UIImage Image { get; }

        public DateTimeOffset CapturedAtUtc { get; }

        public int Width { get; }

        public int Height { get; }

        public void Dispose()
        {
            Image.Dispose();
        }
    }

    private static UIKit.UIWindow? GetActiveWindow()
    {
        foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIKit.UIWindowScene windowScene)
            {
                continue;
            }

            var activeWindow = windowScene.Windows.FirstOrDefault(window => window.IsKeyWindow)
                ?? windowScene.Windows.FirstOrDefault(window => !window.Hidden);
            if (activeWindow != null)
            {
                return activeWindow;
            }
        }

        return null;
    }
#else
    private sealed class SessionJpegCaptureSurface : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private static SessionJpegFrame? EncodeSurface(SessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
        => null;
#endif
}

internal sealed record SessionJpegFrame(
    DateTimeOffset CapturedAtUtc,
    int Width,
    int Height,
    int Quality,
    byte[] Bytes);
