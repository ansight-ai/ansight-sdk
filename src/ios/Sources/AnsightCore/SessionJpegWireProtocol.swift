import Foundation

enum SessionJpegWireProtocol {
    static let headerSize = 28
    static let keyboardPresenceKnownFlag: UInt8 = 1 << 0
    static let keyboardPresentFlag: UInt8 = 1 << 1
    private static let version: UInt8 = 1
    private static let formatJpeg: UInt8 = 1

    static func encode(_ frame: AnsightCapturedScreenFrame) -> Data {
        var payload = Data(count: headerSize + frame.jpegData.count)
        payload.withUnsafeMutableBytes { buffer in
            guard let baseAddress = buffer.baseAddress else {
                return
            }

            let bytes = baseAddress.assumingMemoryBound(to: UInt8.self)
            bytes[0] = UInt8(ascii: "A")
            bytes[1] = UInt8(ascii: "S")
            bytes[2] = UInt8(ascii: "J")
            bytes[3] = UInt8(ascii: "P")
            bytes[4] = version
            bytes[5] = formatJpeg
            bytes[6] = UInt8(max(1, min(frame.quality, 100)))
            bytes[7] = flags(forKeyboardPresence: frame.keyboardPresent)
            writeLittleEndian(frame.capturedAtEpochMilliseconds, to: baseAddress, at: 8)
            writeLittleEndian(Int32(max(0, frame.width)), to: baseAddress, at: 16)
            writeLittleEndian(Int32(max(0, frame.height)), to: baseAddress, at: 20)
            writeLittleEndian(Int32(frame.jpegData.count), to: baseAddress, at: 24)

            frame.jpegData.withUnsafeBytes { jpegBuffer in
                guard let jpegBaseAddress = jpegBuffer.baseAddress else {
                    return
                }

                baseAddress.advanced(by: headerSize).copyMemory(
                    from: jpegBaseAddress,
                    byteCount: frame.jpegData.count
                )
            }
        }
        return payload
    }

    private static func flags(forKeyboardPresence keyboardPresent: Bool?) -> UInt8 {
        guard let keyboardPresent else {
            return 0
        }

        return keyboardPresent
            ? keyboardPresenceKnownFlag | keyboardPresentFlag
            : keyboardPresenceKnownFlag
    }

    private static func writeLittleEndian(_ value: Int32, to baseAddress: UnsafeMutableRawPointer, at offset: Int) {
        var littleEndian = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndian) { bytes in
            baseAddress.advanced(by: offset).copyMemory(from: bytes.baseAddress!, byteCount: bytes.count)
        }
    }

    private static func writeLittleEndian(_ value: Int64, to baseAddress: UnsafeMutableRawPointer, at offset: Int) {
        var littleEndian = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndian) { bytes in
            baseAddress.advanced(by: offset).copyMemory(from: bytes.baseAddress!, byteCount: bytes.count)
        }
    }
}
