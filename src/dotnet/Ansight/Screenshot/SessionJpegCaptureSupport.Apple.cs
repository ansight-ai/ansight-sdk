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
                using var autoreleasePool = new NSAutoreleasePool();
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
        using var autoreleasePool = new NSAutoreleasePool();
        using var imageData = surface.Image.AsJPEG((nfloat)(options.Quality / 100d));
        if (imageData is null)
        {
            return OperationResult.FromSuccess("Session JPEG frame skipped.");
        }

        var jpegByteCount = checked((int)imageData.Length);
        RecordEncodedJpegByteCount(jpegByteCount);

        byte[]? headerBuffer = null;
        byte[]? chunkBuffer = null;
        try
        {
            headerBuffer = ArrayPool<byte>.Shared.Rent(SessionJpegWireProtocol.HeaderSize);
            chunkBuffer = ArrayPool<byte>.Shared.Rent(JpegChunkByteCount);

            SessionJpegWireProtocol.WriteHeader(
                headerBuffer.AsSpan(0, SessionJpegWireProtocol.HeaderSize),
                surface.CapturedAtUtc,
                surface.Width,
                surface.Height,
                options.Quality,
                jpegByteCount);

            return await transport.SendBinaryAsync(
                async (sendFragmentAsync, sendCancellationToken) =>
                {
                    await sendFragmentAsync(
                        headerBuffer.AsMemory(0, SessionJpegWireProtocol.HeaderSize),
                        endOfMessage: jpegByteCount == 0,
                        sendCancellationToken);

                    var payloadOffset = 0;
                    while (payloadOffset < jpegByteCount)
                    {
                        var bytesToSend = Math.Min(chunkBuffer.Length, jpegByteCount - payloadOffset);
                        Marshal.Copy(IntPtr.Add(imageData.Bytes, payloadOffset), chunkBuffer, 0, bytesToSend);
                        payloadOffset += bytesToSend;
                        await sendFragmentAsync(
                            chunkBuffer.AsMemory(0, bytesToSend),
                            endOfMessage: payloadOffset == jpegByteCount,
                            sendCancellationToken);
                    }
                },
                cancellationToken);
        }
        finally
        {
            if (chunkBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(chunkBuffer);
            }

            if (headerBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(headerBuffer);
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
