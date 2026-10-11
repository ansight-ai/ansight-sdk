import Foundation
import Darwin

internal enum OfflineCaptureFileIO {
    static func read(from file: FileHandle) throws -> Data? {
        var buffer = [UInt8](repeating: 0, count: 64 * 1024)
        while true {
            let count = Darwin.read(file.fileDescriptor, &buffer, buffer.count)
            if count < 0 && errno == EINTR { continue }
            guard count >= 0 else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
            return count == 0 ? nil : Data(buffer.prefix(count))
        }
    }
    // FileHandle.write(contentsOf:) requires macOS 10.15.4; the SDK supports 10.15.
    static func write(_ data: Data, to file: FileHandle) throws {
        try data.withUnsafeBytes { buffer in
            guard let base = buffer.baseAddress else { return }
            var offset = 0
            while offset < buffer.count {
                let written = Darwin.write(file.fileDescriptor, base.advanced(by: offset), buffer.count - offset)
                if written < 0 && errno == EINTR { continue }
                guard written > 0 else { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno)) }
                offset += written
            }
        }
    }
}
