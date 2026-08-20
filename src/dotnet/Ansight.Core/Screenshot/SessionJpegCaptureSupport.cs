using System.Buffers;
using System.IO;
using Ansight.Pairing;

namespace Ansight.Screenshot;

internal interface ISessionJpegCaptureSurface : IDisposable
{
    DateTimeOffset CapturedAtUtc { get; }

    bool? KeyboardPresent { get; }
}

internal static partial class SessionJpegCaptureSupport
{
    private static int lastEncodedJpegBytes = 32 * 1024;

    public static Task<ISessionJpegCaptureSurface?> CaptureSurfaceAsync(SessionJpegCaptureOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return CaptureSurfaceCoreAsync(options, cancellationToken);
    }

    public static Task<OperationResult> SendSurfaceAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(transport);

        return SendSurfaceCoreAsync(surface, options, transport, cancellationToken);
    }

    internal static async Task<SessionJpegFrame?> CaptureJpegFrameAsync(
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var surface = await CaptureSurfaceAsync(options, cancellationToken);
        if (surface is null)
        {
            return null;
        }

        using (surface)
        {
            return await EncodeSurfaceCoreAsync(surface, options, cancellationToken);
        }
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

    internal static int EstimateInitialJpegByteCapacity(int width, int height)
    {
        var lastEncodedBytes = Volatile.Read(ref lastEncodedJpegBytes);
        if (lastEncodedBytes > 0)
        {
            return Math.Max(8 * 1024, lastEncodedBytes);
        }

        if (width <= 0 || height <= 0)
        {
            return 32 * 1024;
        }

        return Math.Max(8 * 1024, (width * height) / 2);
    }

    internal static void RecordEncodedJpegByteCount(int jpegByteCount)
    {
        if (jpegByteCount <= 0)
        {
            return;
        }

        Volatile.Write(ref lastEncodedJpegBytes, jpegByteCount);
    }

    private static partial Task<ISessionJpegCaptureSurface?> CaptureSurfaceCoreAsync(
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken);

    private static partial Task<OperationResult> SendSurfaceCoreAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        PairingSessionTransport transport,
        CancellationToken cancellationToken);

    private static partial Task<SessionJpegFrame?> EncodeSurfaceCoreAsync(
        ISessionJpegCaptureSurface surface,
        SessionJpegCaptureOptions options,
        CancellationToken cancellationToken);
}

internal sealed class SessionJpegFrame : IDisposable
{
    private byte[]? buffer;

    public SessionJpegFrame(
        byte[] buffer,
        int length,
        DateTimeOffset capturedAtUtc,
        int width,
        int height,
        int quality,
        int jpegByteCount)
    {
        this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        Length = length;
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        Width = width;
        Height = height;
        Quality = quality;
        JpegByteCount = jpegByteCount;
    }

    public int Length { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public int Width { get; }

    public int Height { get; }

    public int Quality { get; }

    public int JpegByteCount { get; }

    public ReadOnlyMemory<byte> Payload => buffer is null
        ? ReadOnlyMemory<byte>.Empty
        : buffer.AsMemory(0, Length);

    public ReadOnlyMemory<byte> JpegPayload => Payload.Length <= SessionJpegWireProtocol.HeaderSize
        ? ReadOnlyMemory<byte>.Empty
        : Payload[SessionJpegWireProtocol.HeaderSize..];

    public void Dispose()
    {
        var currentBuffer = Interlocked.Exchange(ref buffer, null);
        if (currentBuffer is not null && currentBuffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(currentBuffer);
        }
    }
}

internal sealed class PooledBufferStream : Stream
{
    private byte[] buffer;
    private int length;
    private bool detached;

    public PooledBufferStream(int initialCapacity)
    {
        buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 1024));
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => length;

    public override long Position
    {
        get => length;
        set => throw new NotSupportedException();
    }

    public int LengthWritten => length;

    public void ReservePrefix(int byteCount)
    {
        EnsureCapacity(byteCount);
        length = byteCount;
    }

    public Span<byte> GetWrittenSpan(int byteCount)
    {
        if (byteCount < 0 || byteCount > length)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }

        return buffer.AsSpan(0, byteCount);
    }

    public SessionJpegFrame DetachFrame(
        DateTimeOffset capturedAtUtc,
        int width,
        int height,
        int quality,
        int jpegByteCount)
    {
        var detachedBuffer = buffer;
        var detachedLength = length;
        buffer = Array.Empty<byte>();
        length = 0;
        detached = true;
        return new SessionJpegFrame(
            detachedBuffer,
            detachedLength,
            capturedAtUtc,
            width,
            height,
            quality,
            jpegByteCount);
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

    public override void Write(byte[] sourceBuffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(sourceBuffer);
        if ((uint)offset > sourceBuffer.Length || (uint)count > sourceBuffer.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        Write(sourceBuffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> sourceBuffer)
    {
        EnsureCapacity(sourceBuffer.Length);
        sourceBuffer.CopyTo(buffer.AsSpan(length));
        length += sourceBuffer.Length;
    }

    public override void WriteByte(byte value)
    {
        EnsureCapacity(1);
        buffer[length++] = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (!detached && buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = Array.Empty<byte>();
        }

        base.Dispose(disposing);
    }

    private void EnsureCapacity(int additionalBytes)
    {
        var requiredLength = checked(length + additionalBytes);
        if (requiredLength <= buffer.Length)
        {
            return;
        }

        var expandedBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(requiredLength, buffer.Length * 2));
        buffer.AsSpan(0, length).CopyTo(expandedBuffer);
        ArrayPool<byte>.Shared.Return(buffer);
        buffer = expandedBuffer;
    }
}
