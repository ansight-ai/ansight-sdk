#if ANDROID
using Android.App;
using System.Net.WebSockets;
using Ansight.Pairing;

namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
    private static readonly Android.OS.Handler MainHandler = new(Android.OS.Looper.MainLooper!);
    private static readonly Lock captureStateGate = new();
    private static CaptureBitmapState? reusableCaptureState;

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

    private static partial Task<OperationResult> SendSurfaceCoreAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken)
    {
        return surface is SessionJpegCaptureSurface androidSurface
            ? SendSurfaceAsync(androidSurface, options, transport, cancellationToken)
            : Task.FromResult(OperationResult.FromFailure("Session JPEG capture surface type mismatch."));
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

        var captureState = AcquireCaptureState(targetWidth, targetHeight);
        try
        {
            captureState.Clear();
            var saveCount = captureState.Canvas.Save();
            try
            {
                if (targetWidth != rootView.Width || targetHeight != rootView.Height)
                {
                    captureState.Canvas.Scale(targetWidth / (float)rootView.Width, targetHeight / (float)rootView.Height);
                }

                rootView.Draw(captureState.Canvas);
            }
            finally
            {
                captureState.Canvas.RestoreToCount(saveCount);
            }

            surface = new SessionJpegCaptureSurface(captureState, DateTimeOffset.UtcNow, targetWidth, targetHeight);
            return true;
        }
        catch
        {
            captureState.Release();
            throw;
        }
    }

    private static async Task<OperationResult> SendSurfaceAsync(
        SessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken)
    {
        using var frame = EncodeSurface(surface, options);
        if (frame is null)
        {
            return OperationResult.FromSuccess("Session JPEG frame skipped.");
        }

        return await transport.SendBinaryAsync(frame.Payload, WebSocketMessageType.Binary, cancellationToken);
    }

    private static SessionJpegFrame? EncodeSurface(SessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
    {
        using var stream = new PooledBufferStream(
            SessionJpegWireProtocol.HeaderSize + EstimateInitialJpegByteCapacity(surface.Width, surface.Height));
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

        RecordEncodedJpegByteCount(jpegLength);
        return stream.DetachFrame();
    }

    private static CaptureBitmapState AcquireCaptureState(int width, int height)
    {
        lock (captureStateGate)
        {
            if (reusableCaptureState is not null)
            {
                if (reusableCaptureState.Matches(width, height) && !reusableCaptureState.IsInUse)
                {
                    reusableCaptureState.Acquire();
                    return reusableCaptureState;
                }

                if (!reusableCaptureState.Matches(width, height) && !reusableCaptureState.IsInUse)
                {
                    reusableCaptureState.Dispose();
                    reusableCaptureState = CaptureBitmapState.CreateReusable(width, height);
                    reusableCaptureState.Acquire();
                    return reusableCaptureState;
                }
            }

            if (reusableCaptureState is null)
            {
                reusableCaptureState = CaptureBitmapState.CreateReusable(width, height);
                reusableCaptureState.Acquire();
                return reusableCaptureState;
            }

            var temporaryState = CaptureBitmapState.CreateTemporary(width, height);
            temporaryState.Acquire();
            return temporaryState;
        }
    }

    private sealed class SessionJpegCaptureSurface : ISessionJpegCaptureSurface
    {
        private readonly CaptureBitmapState captureState;

        public SessionJpegCaptureSurface(CaptureBitmapState captureState, DateTimeOffset capturedAtUtc, int width, int height)
        {
            this.captureState = captureState;
            CapturedAtUtc = capturedAtUtc;
            Width = width;
            Height = height;
        }

        public Android.Graphics.Bitmap Bitmap => captureState.Bitmap;

        public DateTimeOffset CapturedAtUtc { get; }

        public int Width { get; }

        public int Height { get; }

        public void Dispose()
        {
            captureState.Release();
        }
    }

    private sealed class CaptureBitmapState : IDisposable
    {
        private bool inUse;

        private CaptureBitmapState(int width, int height, bool reusable)
        {
            Width = width;
            Height = height;
            IsReusable = reusable;
            Bitmap = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888!);
            Canvas = new Android.Graphics.Canvas(Bitmap);
        }

        public Android.Graphics.Bitmap Bitmap { get; }

        public Android.Graphics.Canvas Canvas { get; }

        public int Width { get; }

        public int Height { get; }

        public bool IsReusable { get; }

        public bool IsInUse => inUse;

        public static CaptureBitmapState CreateReusable(int width, int height) => new(width, height, reusable: true);

        public static CaptureBitmapState CreateTemporary(int width, int height) => new(width, height, reusable: false);

        public void Acquire()
        {
            if (inUse)
            {
                throw new InvalidOperationException("Capture bitmap state is already in use.");
            }

            inUse = true;
        }

        public void Clear()
        {
            Canvas.DrawColor(Android.Graphics.Color.Transparent, Android.Graphics.PorterDuff.Mode.Clear!);
        }

        public bool Matches(int width, int height) => Width == width && Height == height;

        public void Release()
        {
            if (IsReusable)
            {
                inUse = false;
                return;
            }

            Dispose();
        }

        public void Dispose()
        {
            Canvas.Dispose();
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
