#if IOS || MACCATALYST
using System.Buffers;
using System.Runtime.InteropServices;
using CoreGraphics;
using Foundation;
using UIKit;
using Ansight.Pairing;

namespace Ansight.Screenshot;

internal static partial class SessionJpegCaptureSupport
{
    private const int JpegChunkByteCount = 64 * 1024;
    private static readonly Lock rendererGate = new();
    private static CachedImageRenderer? cachedImageRenderer;

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
        return surface is SessionJpegCaptureSurface appleSurface
            ? SendSurfaceAsync(appleSurface, options, transport, cancellationToken)
            : Task.FromResult(OperationResult.FromFailure("Session JPEG capture surface type mismatch."));
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

        var targetSize = new CGSize(targetWidth, targetHeight);
        var renderer = GetRenderer(targetWidth, targetHeight, window.Opaque);
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

    private static async Task<OperationResult> SendSurfaceAsync(
        SessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken)
    {
        using var imageData = surface.Image.AsJPEG((nfloat)(options.Quality / 100d));
        if (imageData is null)
        {
            return OperationResult.FromSuccess("Session JPEG frame skipped.");
        }

        var jpegByteCount = checked((int)imageData.Length);
        RecordEncodedJpegByteCount(jpegByteCount);

        var headerBuffer = ArrayPool<byte>.Shared.Rent(SessionJpegWireProtocol.HeaderSize);
        try
        {
            SessionJpegWireProtocol.WriteHeader(
                headerBuffer.AsSpan(0, SessionJpegWireProtocol.HeaderSize),
                surface.CapturedAtUtc,
                surface.Width,
                surface.Height,
                options.Quality,
                jpegByteCount);

            return await transport.SendBinaryAsync(
                (sendFragmentAsync, payloadCancellationToken) => WriteEncodedJpegPayloadAsync(
                    sendFragmentAsync,
                    headerBuffer,
                    imageData,
                    jpegByteCount,
                    payloadCancellationToken),
                cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(headerBuffer);
        }
    }

    private static async Task WriteEncodedJpegPayloadAsync(
        BinaryFragmentSender sendFragmentAsync,
        byte[] headerBuffer,
        NSData imageData,
        int jpegByteCount,
        CancellationToken cancellationToken)
    {
        await sendFragmentAsync(
            headerBuffer.AsMemory(0, SessionJpegWireProtocol.HeaderSize),
            endOfMessage: jpegByteCount == 0,
            cancellationToken);

        var payloadOffset = 0;
        while (payloadOffset < jpegByteCount)
        {
            var bytesToCopy = Math.Min(JpegChunkByteCount, jpegByteCount - payloadOffset);
            var chunkBuffer = ArrayPool<byte>.Shared.Rent(bytesToCopy);
            try
            {
                Marshal.Copy(IntPtr.Add(imageData.Bytes, payloadOffset), chunkBuffer, 0, bytesToCopy);
                payloadOffset += bytesToCopy;

                await sendFragmentAsync(
                    chunkBuffer.AsMemory(0, bytesToCopy),
                    endOfMessage: payloadOffset == jpegByteCount,
                    cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunkBuffer);
            }
        }
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

    private static UIGraphicsImageRenderer GetRenderer(int width, int height, bool opaque)
    {
        lock (rendererGate)
        {
            if (cachedImageRenderer is not null && cachedImageRenderer.Matches(width, height, opaque))
            {
                return cachedImageRenderer.Renderer;
            }

            cachedImageRenderer?.Dispose();
            cachedImageRenderer = new CachedImageRenderer(width, height, opaque);
            return cachedImageRenderer.Renderer;
        }
    }

    private sealed class SessionJpegCaptureSurface : ISessionJpegCaptureSurface
    {
        private UIImage? image;

        public SessionJpegCaptureSurface(UIImage image, DateTimeOffset capturedAtUtc, int width, int height)
        {
            this.image = image;
            CapturedAtUtc = capturedAtUtc;
            Width = width;
            Height = height;
        }

        public UIImage Image => image ?? throw new ObjectDisposedException(nameof(SessionJpegCaptureSurface));

        public DateTimeOffset CapturedAtUtc { get; }

        public int Width { get; }

        public int Height { get; }

        public void Dispose()
        {
            var currentImage = Interlocked.Exchange(ref image, null);
            if (currentImage is null)
            {
                return;
            }

            if (NSThread.Current.IsMainThread)
            {
                currentImage.Dispose();
                return;
            }

            UIApplication.SharedApplication.BeginInvokeOnMainThread(currentImage.Dispose);
        }
    }

    private sealed class CachedImageRenderer : IDisposable
    {
        public CachedImageRenderer(int width, int height, bool opaque)
        {
            Width = width;
            Height = height;
            Opaque = opaque;

            var rendererFormat = UIGraphicsImageRendererFormat.DefaultFormat;
            rendererFormat.Opaque = opaque;
            rendererFormat.Scale = 1;
            Renderer = new UIGraphicsImageRenderer(new CGSize(width, height), rendererFormat);
        }

        public int Width { get; }

        public int Height { get; }

        public bool Opaque { get; }

        public UIGraphicsImageRenderer Renderer { get; }

        public bool Matches(int width, int height, bool opaque)
        {
            return Width == width && Height == height && Opaque == opaque;
        }

        public void Dispose()
        {
            Renderer.Dispose();
        }
    }
}
#endif
