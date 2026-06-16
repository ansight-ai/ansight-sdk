import Foundation
import Network

extension Data {
    mutating func writeLittleEndian(_ value: Int32, at index: Int) {
        var littleEndian = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndian) { bytes in
            replaceSubrange(index..<index + MemoryLayout<Int32>.size, with: bytes)
        }
    }

    mutating func writeLittleEndian(_ value: Int64, at index: Int) {
        var littleEndian = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndian) { bytes in
            replaceSubrange(index..<index + MemoryLayout<Int64>.size, with: bytes)
        }
    }
}
