import Foundation

internal enum AnsightFileSystemIO {
    static func read(path: String, offsetBytes: Int64 = 0, maxBytes: Int) throws -> Data {
        let handle = try FileHandle(forReadingFrom: URL(fileURLWithPath: path))
        defer {
            try? handle.close()
        }

        if offsetBytes > 0 {
            try handle.seek(toOffset: UInt64(offsetBytes))
        }

        return try handle.read(upToCount: maxBytes) ?? Data()
    }

    static func readAll(path: String) throws -> Data {
        try Data(contentsOf: URL(fileURLWithPath: path), options: [.mappedIfSafe])
    }

    static func attributes(path: String) throws -> [FileAttributeKey: Any] {
        try FileManager.default.attributesOfItem(atPath: path)
    }
}
