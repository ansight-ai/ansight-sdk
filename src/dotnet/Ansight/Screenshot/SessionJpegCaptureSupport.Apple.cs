#if IOS || MACCATALYST
using UIKit;

namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
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
        return surface is SessionJpegCaptureSurface appleSurface
            ? EncodeSurface(appleSurface, options)
            : null;
    }

    private static Task<T?> InvokeOnUiThreadAsync<T>(Func<T?> capture)
    {
        var taskCompletionSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
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

        var renderScale = GetRenderScale(window);
        var sourcePixelWidth = (int)Math.Round(originalBounds.Width * renderScale);
        var sourcePixelHeight = (int)Math.Round(originalBounds.Height * renderScale);

        var targetWidth = ResolveTargetWidth(sourcePixelWidth, options.MaxWidth);
        var targetHeight = ResolveScaledHeight(sourcePixelWidth, sourcePixelHeight, targetWidth);
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            surface = null;
            return false;
        }

        var targetSize = new CoreGraphics.CGSize(targetWidth, targetHeight);
        var rendererFormat = UIGraphicsImageRendererFormat.DefaultFormat;
        rendererFormat.Opaque = window.Opaque;
        rendererFormat.Scale = 1;

        using var renderer = new UIGraphicsImageRenderer(targetSize, rendererFormat);
        var image = renderer.CreateImage(renderContext =>
        {
            renderContext.CGContext.ScaleCTM(
                (nfloat)(targetSize.Width / originalBounds.Width),
                (nfloat)(targetSize.Height / originalBounds.Height));
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

    private static UIWindow? GetActiveWindow()
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is not UIWindowScene windowScene)
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

    private static nfloat GetRenderScale(UIWindow window)
    {
        var scale = window.Screen?.Scale ?? UIScreen.MainScreen.Scale;
        return scale > 0 ? scale : 1;
    }

    private sealed class SessionJpegCaptureSurface : ISessionJpegCaptureSurface
    {
        public SessionJpegCaptureSurface(UIImage image, DateTimeOffset capturedAtUtc, int width, int height)
        {
            Image = image;
            CapturedAtUtc = capturedAtUtc;
            Width = width;
            Height = height;
        }

        public UIImage Image { get; }

        public DateTimeOffset CapturedAtUtc { get; }

        public int Width { get; }

        public int Height { get; }

        public void Dispose()
        {
            Image.Dispose();
        }
    }
}
#endif
