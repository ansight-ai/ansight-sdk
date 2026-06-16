import Foundation
import Network

public enum PairingFileTransferWireProtocol {
    public static let protocolName = "ansight.file-transfer.v1"
    public static let headerSize = 56
    public static let version: UInt8 = 1
    public static let magic = "ASFT"

    public static func createFrame(
        transferId: UUID,
        frameType: PairingFileTransferFrameType,
        sequence: Int32,
        offsetBytes: Int64,
        payload: Data
    ) -> Data {
        var frame = Data(count: headerSize + payload.count)
        writeHeader(
            into: &frame,
            transferId: transferId,
            frameType: frameType,
            sequence: sequence,
            offsetBytes: offsetBytes,
            payloadByteCount: Int32(payload.count)
        )
        frame.replaceSubrange(headerSize..<headerSize + payload.count, with: payload)
        return frame
    }

    public static func writeHeader(
        into header: inout Data,
        transferId: UUID,
        frameType: PairingFileTransferFrameType,
        sequence: Int32,
        offsetBytes: Int64,
        payloadByteCount: Int32
    ) {
        precondition(header.count >= headerSize, "The file transfer header buffer was too small.")
        let magicBytes = Array(magic.utf8)
        header[0] = magicBytes[0]
        header[1] = magicBytes[1]
        header[2] = magicBytes[2]
        header[3] = magicBytes[3]
        header[4] = version
        header[5] = frameType.rawValue
        header[6] = 0
        header[7] = 0

        let transferIdBytes = Array(transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased().utf8)
        header.replaceSubrange(8..<40, with: transferIdBytes.prefix(32))
        header.writeLittleEndian(sequence, at: 40)
        header.writeLittleEndian(offsetBytes, at: 44)
        header.writeLittleEndian(payloadByteCount, at: 52)
    }
}
