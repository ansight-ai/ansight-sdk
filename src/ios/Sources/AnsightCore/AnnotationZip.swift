import Foundation

/// Writes uncompressed ZIP entries so the host can read the standard annotation bundle.
internal enum AnnotationZip {
    internal enum Error: Swift.Error { case archiveTooLarge }

    internal static func make(_ entries: [(String, Data)]) throws -> Data {
        var archive = Data()
        var central = Data()
        for (path, contents) in entries {
            let name = Data(path.utf8)
            guard let offset = UInt32(exactly: archive.count),
                  let size = UInt32(exactly: contents.count),
                  let nameLength = UInt16(exactly: name.count) else {
                throw Error.archiveTooLarge
            }
            let checksum = crc32(contents)
            archive.appendLE(UInt32(0x04034b50))
            archive.appendLE(UInt16(20))
            archive.appendLE(UInt16(0x0800))
            archive.appendLE(UInt16(0))
            archive.appendLE(UInt16(0))
            archive.appendLE(UInt16(0x0021))
            archive.appendLE(checksum)
            archive.appendLE(size)
            archive.appendLE(size)
            archive.appendLE(nameLength)
            archive.appendLE(UInt16(0))
            archive.append(name)
            archive.append(contents)

            central.appendLE(UInt32(0x02014b50))
            central.appendLE(UInt16(20))
            central.appendLE(UInt16(20))
            central.appendLE(UInt16(0x0800))
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0x0021))
            central.appendLE(checksum)
            central.appendLE(size)
            central.appendLE(size)
            central.appendLE(nameLength)
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0))
            central.appendLE(UInt16(0))
            central.appendLE(UInt32(0))
            central.appendLE(offset)
            central.append(name)
        }
        guard let count = UInt16(exactly: entries.count),
              let centralOffset = UInt32(exactly: archive.count),
              let centralSize = UInt32(exactly: central.count) else {
            throw Error.archiveTooLarge
        }
        archive.append(central)
        archive.appendLE(UInt32(0x06054b50))
        archive.appendLE(UInt16(0))
        archive.appendLE(UInt16(0))
        archive.appendLE(count)
        archive.appendLE(count)
        archive.appendLE(centralSize)
        archive.appendLE(centralOffset)
        archive.appendLE(UInt16(0))
        return archive
    }

    private static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xffff_ffff
        for byte in data {
            crc ^= UInt32(byte)
            for _ in 0..<8 {
                crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb8_8320 : 0)
            }
        }
        return crc ^ 0xffff_ffff
    }
}

private extension Data {
    mutating func appendLE(_ value: UInt16) {
        append(UInt8(truncatingIfNeeded: value))
        append(UInt8(truncatingIfNeeded: value >> 8))
    }

    mutating func appendLE(_ value: UInt32) {
        append(UInt8(truncatingIfNeeded: value))
        append(UInt8(truncatingIfNeeded: value >> 8))
        append(UInt8(truncatingIfNeeded: value >> 16))
        append(UInt8(truncatingIfNeeded: value >> 24))
    }
}
