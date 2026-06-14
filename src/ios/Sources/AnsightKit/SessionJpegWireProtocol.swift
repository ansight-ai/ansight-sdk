import Foundation

enum SessionJpegWireProtocol {
    static let headerSize = 28
    private static let version: UInt8 = 1
    private static let formatJpeg: UInt8 = 1

    static func encode(_ frame: AnsightCapturedScreenFrame) -> Data {
        var payload = Data(count: headerSize + frame.jpegData.count)
        payload[0] = UInt8(ascii: "A")
        payload[1] = UInt8(ascii: "S")
        payload[2] = UInt8(ascii: "J")
        payload[3] = UInt8(ascii: "P")
        payload[4] = version
        payload[5] = formatJpeg
        payload[6] = UInt8(max(1, min(frame.quality, 100)))
        payload.writeLittleEndian(frame.capturedAtEpochMilliseconds, at: 8)
        payload.writeLittleEndian(Int32(max(0, frame.width)), at: 16)
        payload.writeLittleEndian(Int32(max(0, frame.height)), at: 20)
        payload.writeLittleEndian(Int32(frame.jpegData.count), at: 24)
        payload.replaceSubrange(headerSize..<payload.count, with: frame.jpegData)
        return payload
    }
}
