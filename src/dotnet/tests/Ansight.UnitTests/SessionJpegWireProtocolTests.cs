using System.Buffers.Binary;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class SessionJpegWireProtocolTests
{
    [Fact]
    public void WriteHeader_WritesExpectedBinaryLayout()
    {
        Span<byte> header = stackalloc byte[SessionJpegWireProtocol.HeaderSize];
        var capturedAtUtc = new DateTimeOffset(2025, 02, 03, 04, 05, 06, TimeSpan.Zero);

        SessionJpegWireProtocol.WriteHeader(header, capturedAtUtc, width: 640, height: 360, quality: 85, jpegByteCount: 12345);

        Assert.Equal((byte)'A', header[0]);
        Assert.Equal((byte)'S', header[1]);
        Assert.Equal((byte)'J', header[2]);
        Assert.Equal((byte)'P', header[3]);
        Assert.Equal(1, header[4]);
        Assert.Equal(1, header[5]);
        Assert.Equal(85, header[6]);
        Assert.Equal(0, header[7]);
        Assert.Equal(capturedAtUtc.ToUnixTimeMilliseconds(), BinaryPrimitives.ReadInt64LittleEndian(header[8..16]));
        Assert.Equal(640, BinaryPrimitives.ReadInt32LittleEndian(header[16..20]));
        Assert.Equal(360, BinaryPrimitives.ReadInt32LittleEndian(header[20..24]));
        Assert.Equal(12345, BinaryPrimitives.ReadInt32LittleEndian(header[24..28]));
    }

    [Theory]
    [InlineData(false, SessionJpegWireProtocol.KeyboardPresenceKnownFlag)]
    [InlineData(true, SessionJpegWireProtocol.KeyboardPresenceKnownFlag | SessionJpegWireProtocol.KeyboardPresentFlag)]
    public void WriteHeader_WritesKeyboardPresenceFlags(bool keyboardPresent, byte expectedFlags)
    {
        Span<byte> header = stackalloc byte[SessionJpegWireProtocol.HeaderSize];

        SessionJpegWireProtocol.WriteHeader(
            header,
            DateTimeOffset.UtcNow,
            width: 1,
            height: 1,
            quality: 60,
            jpegByteCount: 0,
            keyboardPresent: keyboardPresent);

        Assert.Equal(expectedFlags, header[7]);
    }

    [Fact]
    public void WriteHeader_ThrowsWhenBufferIsTooSmall()
    {
        var header = new byte[SessionJpegWireProtocol.HeaderSize - 1];

        var exception = Assert.Throws<ArgumentException>(() =>
            SessionJpegWireProtocol.WriteHeader(header, DateTimeOffset.UtcNow, 100, 100, 80, 1024));

        Assert.Contains("too small", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
