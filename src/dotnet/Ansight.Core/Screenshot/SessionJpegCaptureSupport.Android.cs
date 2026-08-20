#if ANDROID
using System.Net.WebSockets;
using Ansight.Pairing;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
    private static readonly Android.OS.Handler mainHandler = new(Android.OS.Looper.MainLooper!);
    private static readonly Lock captureStateGate = new();
    private static CaptureBitmapState? reusableCaptureState;

    private static partial Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync<ISessionJpegCaptureSurface?>(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await CaptureSurfaceBitmapAsync(options, cancellationToken);
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

    private static partial Task<SessionJpegFrame?> EncodeSurfaceCoreAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return surface is SessionJpegCaptureSurface androidSurface
            ? Task.FromResult(EncodeSurface(androidSurface, options))
            : Task.FromResult<SessionJpegFrame?>(null);
    }

    private static Task<T?> InvokeOnUiThreadAsync<T>(Func<T?> capture)
    {
        var taskCompletionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        mainHandler.Post(() =>
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

    private static Task<T?> InvokeOnUiThreadAsync<T>(Func<Task<T?>> capture)
    {
        var taskCompletionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        mainHandler.Post(async () =>
        {
            try
            {
                taskCompletionSource.SetResult(await capture());
            }
            catch (Exception ex)
            {
                taskCompletionSource.SetException(ex);
            }
        });

        return taskCompletionSource.Task;
    }

    private static async Task<SessionJpegCaptureSurface?> CaptureSurfaceBitmapAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        var captureRoot = AndroidSceneCapture.GetCurrentRoot();
        if (captureRoot == null)
        {
            return null;
        }

        var rootView = captureRoot.RootView;
        var targetWidth = ResolveTargetWidth(rootView.Width, options.MaxWidth);
        var targetHeight = ResolveScaledHeight(rootView.Width, rootView.Height, targetWidth);
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return null;
        }

        var captureState = AcquireCaptureState(targetWidth, targetHeight);
        try
        {
            captureState.Clear();
            var captureResult = await AndroidSceneCapture.CaptureAsync(
                captureRoot,
                captureState.Bitmap,
                captureState.Canvas,
                captureState.WindowBitmap,
                cancellationToken);
            if (!captureResult.Success)
            {
                captureState.Release();
                return null;
            }

            return new SessionJpegCaptureSurface(
                captureState,
                DateTimeOffset.UtcNow,
                targetWidth,
                targetHeight,
                options.CaptureKeyboardPresence ? IsKeyboardPresent(rootView) : null);
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
            jpegLength,
            surface.KeyboardPresent);

        RecordEncodedJpegByteCount(jpegLength);
        return stream.DetachFrame(
            surface.CapturedAtUtc,
            surface.Width,
            surface.Height,
            options.Quality,
            jpegLength);
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

        public SessionJpegCaptureSurface(
            CaptureBitmapState captureState,
            DateTimeOffset capturedAtUtc,
            int width,
            int height,
            bool? keyboardPresent)
        {
            this.captureState = captureState;
            CapturedAtUtc = capturedAtUtc;
            Width = width;
            Height = height;
            KeyboardPresent = keyboardPresent;
        }

        public Android.Graphics.Bitmap Bitmap => captureState.Bitmap;

        public DateTimeOffset CapturedAtUtc { get; }

        public int Width { get; }

        public int Height { get; }

        public bool? KeyboardPresent { get; }

        public void Dispose()
        {
            captureState.Release();
        }
    }

    private static bool IsKeyboardPresent(View rootView)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var windowInsets = rootView.RootWindowInsets;
            if (windowInsets is not null)
            {
                return windowInsets.IsVisible(WindowInsets.Type.Ime());
            }
        }

        var visibleFrame = new Rect();
        rootView.GetWindowVisibleDisplayFrame(visibleFrame);
        var rootHeight = rootView.RootView?.Height ?? rootView.Height;
        if (rootHeight <= 0)
        {
            return false;
        }

        var obscuredHeight = Math.Max(0, rootHeight - visibleFrame.Bottom);
        var density = rootView.Resources?.DisplayMetrics?.Density ?? 1f;
        var minimumKeyboardHeight = Math.Max((int)(100 * density), (int)(rootHeight * 0.15f));
        return obscuredHeight > minimumKeyboardHeight;
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
            WindowBitmap = Android.Graphics.Bitmap.CreateBitmap(width, height, Android.Graphics.Bitmap.Config.Argb8888!);
            Canvas = new Android.Graphics.Canvas(Bitmap);
        }

        public Android.Graphics.Bitmap Bitmap { get; }

        public Android.Graphics.Bitmap WindowBitmap { get; }

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
                lock (captureStateGate)
                {
                    inUse = false;
                }

                return;
            }

            Dispose();
        }

        public void Dispose()
        {
            Canvas.Dispose();
            WindowBitmap.Dispose();
            Bitmap.Dispose();
        }
    }

}
#endif
