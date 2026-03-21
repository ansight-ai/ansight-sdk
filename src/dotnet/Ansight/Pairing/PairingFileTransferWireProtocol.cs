using System.Buffers.Binary;
using System.Text;

namespace Ansight.Pairing;

internal enum PairingFileTransferFrameType : byte
{
    Chunk = 1,
    Complete = 2,
    Error = 3
}

internal static class PairingFileTransferWireProtocol
{
    internal const string ProtocolName = "ansight.file-transfer.v1";
    internal const int HeaderSize = 56;
    private const byte Version = 1;

    internal static byte[] CreateFrame(
        Guid transferId,
        PairingFileTransferFrameType frameType,
        int sequence,
        long offsetBytes,
        ReadOnlySpan<byte> payload)
    {
        var frame = new byte[HeaderSize + payload.Length];
        WriteHeader(frame, transferId, frameType, sequence, offsetBytes, payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    internal static void WriteHeader(
        Span<byte> header,
        Guid transferId,
        PairingFileTransferFrameType frameType,
        int sequence,
        long offsetBytes,
        int payloadByteCount)
    {
        if (header.Length < HeaderSize)
        {
            throw new ArgumentException("The file transfer header buffer was too small.", nameof(header));
        }

        header[0] = (byte)'A';
        header[1] = (byte)'S';
        header[2] = (byte)'F';
        header[3] = (byte)'T';
        header[4] = Version;
        header[5] = (byte)frameType;
        header[6] = 0;
        header[7] = 0;

        var transferIdBytes = Encoding.ASCII.GetBytes(transferId.ToString("N"));
        transferIdBytes.CopyTo(header[8..40]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..44], sequence);
        BinaryPrimitives.WriteInt64LittleEndian(header[44..52], offsetBytes);
        BinaryPrimitives.WriteInt32LittleEndian(header[52..56], payloadByteCount);
    }
}
