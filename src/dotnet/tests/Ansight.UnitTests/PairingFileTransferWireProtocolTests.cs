using System.Buffers.Binary;
using System.Text;
using Ansight.Pairing;

namespace Ansight.UnitTests;

public sealed class PairingFileTransferWireProtocolTests
{
    [Fact]
    public void WriteHeader_WritesExpectedBinaryLayout()
    {
        Span<byte> header = stackalloc byte[PairingFileTransferWireProtocol.HeaderSize];
        var transferId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        PairingFileTransferWireProtocol.WriteHeader(
            header,
            transferId,
            PairingFileTransferFrameType.Chunk,
            sequence: 7,
            offsetBytes: 4096,
            payloadByteCount: 1234);

        Assert.Equal((byte)'A', header[0]);
        Assert.Equal((byte)'S', header[1]);
        Assert.Equal((byte)'F', header[2]);
        Assert.Equal((byte)'T', header[3]);
        Assert.Equal(1, header[4]);
        Assert.Equal((byte)PairingFileTransferFrameType.Chunk, header[5]);
        Assert.Equal("0123456789abcdef0123456789abcdef", Encoding.ASCII.GetString(header[8..40]));
        Assert.Equal(7, BinaryPrimitives.ReadInt32LittleEndian(header[40..44]));
        Assert.Equal(4096L, BinaryPrimitives.ReadInt64LittleEndian(header[44..52]));
        Assert.Equal(1234, BinaryPrimitives.ReadInt32LittleEndian(header[52..56]));
    }

    [Fact]
    public void WriteHeader_ThrowsWhenBufferIsTooSmall()
    {
        var header = new byte[PairingFileTransferWireProtocol.HeaderSize - 1];

        var exception = Assert.Throws<ArgumentException>(() =>
            PairingFileTransferWireProtocol.WriteHeader(
                header,
                Guid.NewGuid(),
                PairingFileTransferFrameType.Complete,
                sequence: 0,
                offsetBytes: 0,
                payloadByteCount: 0));

        Assert.Contains("too small", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
