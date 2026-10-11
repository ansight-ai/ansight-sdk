import Foundation
import CryptoKit
import zlib

/// Streaming ZIP export of a leased, sealed session. Memory is bounded by one file
/// chunk and the ZIP directory. Entries are stable across retries.
public enum OfflineCaptureArchive {
    public struct Export: Sendable {
        public let path: URL
        public let sha256: String
        public let byteCount: Int64
    }

    public static func write(_ lease: OfflineCaptureLease, to destination: URL) throws -> Export {
        let fileManager = FileManager.default
        let rootPath = lease.directory.resolvingSymlinksInPath().standardizedFileURL.path
        guard !destination.resolvingSymlinksInPath().standardizedFileURL.path.hasPrefix(rootPath + "/") else {
            throw OfflineCaptureError.invalidSession
        }
        guard let enumerator = fileManager.enumerator(at: lease.directory,
            includingPropertiesForKeys: [.isRegularFileKey, .isSymbolicLinkKey]) else {
            throw CocoaError(.fileReadUnknown)
        }
        var files: [URL] = []
        for case let file as URL in enumerator {
            let values = try file.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey])
            if values.isSymbolicLink == true { enumerator.skipDescendants(); continue }
            if values.isRegularFile == true && file.pathExtension != "tmp" { files.append(file) }
        }
        files.sort { $0.path < $1.path }
        guard files.count <= Int(UInt16.max) else { throw OfflineCaptureError.archiveTooLarge }
        try fileManager.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
        let temporary = destination.appendingPathExtension(UUID().uuidString + ".part")
        guard fileManager.createFile(atPath: temporary.path, contents: nil) else { throw CocoaError(.fileWriteUnknown) }
        defer { try? fileManager.removeItem(at: temporary) }
        let output = try FileHandle(forWritingTo: temporary)
        defer { try? output.close() }
        var offset: UInt64 = 0
        var hash = SHA256()
        func write(_ data: Data) throws {
            try OfflineCaptureFileIO.write(data, to: output)
            hash.update(data: data)
            offset += UInt64(data.count)
        }
        var central = Data()
        for file in files {
            let filePath = file.resolvingSymlinksInPath().standardizedFileURL.path
            guard filePath.hasPrefix(rootPath + "/") else { throw OfflineCaptureError.invalidSession }
            let path = String(filePath.dropFirst(rootPath.count + 1))
            let name = Data(path.utf8)
            guard let nameLength = UInt16(exactly: name.count), let entryOffset = UInt32(exactly: offset) else {
                throw OfflineCaptureError.archiveTooLarge
            }
            let input = try FileHandle(forReadingFrom: file)
            defer { try? input.close() }
            var checksum: uLong = 0
            var size: UInt64 = 0
            while let chunk = try OfflineCaptureFileIO.read(from: input) {
                checksum = chunk.withUnsafeBytes { crc32(checksum, $0.bindMemory(to: Bytef.self).baseAddress, uInt(chunk.count)) }
                size += UInt64(chunk.count)
            }
            guard let entrySize = UInt32(exactly: size) else { throw OfflineCaptureError.archiveTooLarge }
            try input.seek(toOffset: 0)
            var header = Data()
            header.littleEndian(UInt32(0x04034b50))
            header.littleEndian(UInt16(20)); header.littleEndian(UInt16(0x0800))
            header.littleEndian(UInt16(0)); header.littleEndian(UInt16(0)); header.littleEndian(UInt16(0x0021))
            header.littleEndian(UInt32(checksum)); header.littleEndian(entrySize); header.littleEndian(entrySize)
            header.littleEndian(nameLength); header.littleEndian(UInt16(0)); header.append(name)
            try write(header)
            while let chunk = try OfflineCaptureFileIO.read(from: input) { try write(chunk) }
            central.littleEndian(UInt32(0x02014b50))
            central.littleEndian(UInt16(20)); central.littleEndian(UInt16(20)); central.littleEndian(UInt16(0x0800))
            central.littleEndian(UInt16(0)); central.littleEndian(UInt16(0)); central.littleEndian(UInt16(0x0021))
            central.littleEndian(UInt32(checksum)); central.littleEndian(entrySize); central.littleEndian(entrySize)
            central.littleEndian(nameLength); central.littleEndian(UInt16(0)); central.littleEndian(UInt16(0))
            central.littleEndian(UInt16(0)); central.littleEndian(UInt16(0)); central.littleEndian(UInt32(0))
            central.littleEndian(entryOffset); central.append(name)
        }
        guard let centralOffset = UInt32(exactly: offset), let centralSize = UInt32(exactly: central.count) else {
            throw OfflineCaptureError.archiveTooLarge
        }
        try write(central)
        var end = Data()
        end.littleEndian(UInt32(0x06054b50)); end.littleEndian(UInt16(0)); end.littleEndian(UInt16(0))
        end.littleEndian(UInt16(files.count)); end.littleEndian(UInt16(files.count))
        end.littleEndian(centralSize); end.littleEndian(centralOffset); end.littleEndian(UInt16(0))
        try write(end)
        try output.synchronize()
        try output.close()
        try fileManager.moveItem(at: temporary, to: destination)
        return Export(path: destination, sha256: hash.finalize().map { String(format: "%02x", $0) }.joined(), byteCount: Int64(offset))
    }
}

private extension Data {
    mutating func littleEndian<T: FixedWidthInteger>(_ value: T) {
        var littleEndianValue = value.littleEndian
        Swift.withUnsafeBytes(of: &littleEndianValue) { append(contentsOf: $0) }
    }
}
