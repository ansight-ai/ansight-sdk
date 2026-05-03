using System.Buffers.Binary;

namespace Ansight.Screenshot;

internal static class SessionJpegWireProtocol
{
    internal const int HeaderSize = 28;
    private const byte Version = 1;
    private const byte FormatJpeg = 1;

    public static void WriteHeader(
        Span<byte> header,
        DateTimeOffset capturedAtUtc,
        int width,
        int height,
        int quality,
        int jpegByteCount)
    {
        if (header.Length < HeaderSize)
        {
            throw new ArgumentException("The JPEG header buffer was too small.", nameof(header));
        }

        header[0] = (byte)'A';
        header[1] = (byte)'S';
        header[2] = (byte)'J';
        header[3] = (byte)'P';
        header[4] = Version;
        header[5] = FormatJpeg;
        header[6] = checked((byte)quality);
        header[7] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(header[8..16], capturedAtUtc.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..24], height);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..28], jpegByteCount);
    }
}
