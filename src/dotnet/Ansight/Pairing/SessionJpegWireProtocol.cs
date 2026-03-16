using System.Buffers.Binary;

namespace Ansight.Pairing;

internal static class SessionJpegWireProtocol
{
    private const int HeaderSize = 28;
    private const byte Version = 1;
    private const byte FormatJpeg = 1;

    public static byte[] Serialize(SessionJpegFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var payload = new byte[HeaderSize + frame.Bytes.Length];
        var header = payload.AsSpan(0, HeaderSize);
        header[0] = (byte)'A';
        header[1] = (byte)'S';
        header[2] = (byte)'J';
        header[3] = (byte)'P';
        header[4] = Version;
        header[5] = FormatJpeg;
        header[6] = checked((byte)frame.Quality);
        header[7] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(header[8..16], frame.CapturedAtUtc.ToUnixTimeMilliseconds());
        BinaryPrimitives.WriteInt32LittleEndian(header[16..20], frame.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..24], frame.Height);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..28], frame.Bytes.Length);
        frame.Bytes.CopyTo(payload, HeaderSize);
        return payload;
    }
}
