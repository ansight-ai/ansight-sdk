using System.Buffers;
using System.IO;

namespace Ansight.Pairing;

internal interface ISessionJpegCaptureSurface : IDisposable
{
    DateTimeOffset CapturedAtUtc { get; }
}

internal static class SessionJpegCaptureSupport
{
    public static Task<ISessionJpegCaptureSurface?> CaptureSurfaceAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return CaptureSurfaceCoreAsync(options, cancellationToken);
    }

    public static SessionJpegFrame? EncodeSurface(ISessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(options);

#if ANDROID
        return surface is SessionJpegCaptureSurface androidSurface
            ? EncodeSurface(androidSurface, options)
            : null;
#elif IOS || MACCATALYST
        return surface is SessionJpegCaptureSurface appleSurface
            ? EncodeSurface(appleSurface, options)
            : null;
#else
        return null;
#endif
    }

    private static int ResolveTargetWidth(int sourceWidth, int? maxWidth)
    {
        if (sourceWidth <= 0)
        {
            return 0;
        }

        if (!maxWidth.HasValue || maxWidth.Value >= sourceWidth)
        {
            return sourceWidth;
        }

        return maxWidth.Value;
    }

    private static int ResolveScaledHeight(int sourceWidth, int sourceHeight, int targetWidth)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0)
        {
            return 0;
        }

        if (targetWidth >= sourceWidth)
        {
            return sourceHeight;
        }

        return Math.Max(1, (int)Math.Round(sourceHeight * (targetWidth / (double)sourceWidth)));
    }

    private static int EstimateInitialPayloadCapacity(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return 32 * 1024;
        }

        var estimatedJpegBytes = Math.Max(8 * 1024, (width * height) / 2);
        return SessionJpegWireProtocol.HeaderSize + estimatedJpegBytes;
    }

#if ANDROID
    private static readonly Android.OS.Handler MainHandler = new(Android.OS.Looper.MainLooper!);

    private static Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync<ISessionJpegCaptureSurface?>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return TryCaptureSurface(options, out var surface) ? surface : null;
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
    private static Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        return InvokeOnUiThreadAsync<ISessionJpegCaptureSurface?>(() =>
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
    private static Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
        => Task.FromResult<ISessionJpegCaptureSurface?>(null);
#endif

#if ANDROID
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

        var targetWidth = ResolveTargetWidth((int)Math.Round(originalBounds.Width), options.MaxWidth);
        var targetHeight = ResolveScaledHeight(
            (int)Math.Round(originalBounds.Width),
            (int)Math.Round(originalBounds.Height),
            targetWidth);
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            surface = null;
            return false;
        }

        var targetSize = new CoreGraphics.CGSize(targetWidth, targetHeight);
        using var renderer = new UIKit.UIGraphicsImageRenderer(targetSize);
        var image = renderer.CreateImage(renderContext =>
        {
            renderContext.CGContext.ScaleCTM((nfloat)(targetSize.Width / originalBounds.Width), (nfloat)(targetSize.Height / originalBounds.Height));
            window.DrawViewHierarchy(originalBounds, afterScreenUpdates: false);
        });

        surface = new SessionJpegCaptureSurface(
            image,
            DateTimeOffset.UtcNow,
            targetWidth,
            targetHeight);
        return true;
    }

    private static SessionJpegFrame? EncodeSurface(SessionJpegCaptureSurface surface, SessionJpegCaptureOptions options)
    {
        using var imageData = surface.Image.AsJPEG((nfloat)(options.Quality / 100d));
        if (imageData is null)
        {
            return null;
        }

        using var stream = new PooledBufferStream(SessionJpegWireProtocol.HeaderSize + checked((int)imageData.Length));
        stream.ReservePrefix(SessionJpegWireProtocol.HeaderSize);
        using var dataStream = imageData.AsStream();
        Span<byte> copyBuffer = stackalloc byte[8192];
        while (true)
        {
            var bytesRead = dataStream.Read(copyBuffer);
            if (bytesRead <= 0)
            {
                break;
            }

            stream.Write(copyBuffer[..bytesRead]);
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
#endif
}

internal sealed class SessionJpegFrame : IDisposable
{
    private byte[]? _buffer;

    public SessionJpegFrame(byte[] buffer, int length)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Length = length;
    }

    public int Length { get; }

    public ReadOnlyMemory<byte> Payload => _buffer is null
        ? ReadOnlyMemory<byte>.Empty
        : _buffer.AsMemory(0, Length);

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null && buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal sealed class PooledBufferStream : Stream
{
    private byte[] _buffer;
    private int _length;
    private bool _detached;

    public PooledBufferStream(int initialCapacity)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 1024));
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _length;

    public override long Position
    {
        get => _length;
        set => throw new NotSupportedException();
    }

    public int LengthWritten => _length;

    public void ReservePrefix(int byteCount)
    {
        EnsureCapacity(byteCount);
        _length = byteCount;
    }

    public Span<byte> GetWrittenSpan(int byteCount)
    {
        if (byteCount < 0 || byteCount > _length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        return _buffer.AsSpan(0, byteCount);
    }

    public SessionJpegFrame DetachFrame()
    {
        var buffer = _buffer;
        var length = _length;
        _buffer = Array.Empty<byte>();
        _length = 0;
        _detached = true;
        return new SessionJpegFrame(buffer, length);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();

    public override void SetLength(long value)
        => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)offset > buffer.Length || (uint)count > buffer.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(buffer.Length);
        buffer.CopyTo(_buffer.AsSpan(_length));
        _length += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_length++] = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (!_detached && _buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = Array.Empty<byte>();
        }

        base.Dispose(disposing);
    }

    private void EnsureCapacity(int additionalBytes)
    {
        var requiredLength = checked(_length + additionalBytes);
        if (requiredLength <= _buffer.Length)
        {
            return;
        }

        var expandedBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(requiredLength, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(expandedBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = expandedBuffer;
    }
}
