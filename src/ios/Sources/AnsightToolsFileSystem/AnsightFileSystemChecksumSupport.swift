import CryptoKit
import Foundation

internal enum AnsightFileSystemChecksumSupport {
    private static let allAlgorithms = ["md5", "sha1", "sha256", "sha384", "sha512", "crc32"]
    private static let crc32Table: [UInt32] = {
        (0..<256).map { value in
            var checksum = UInt32(value)
            for _ in 0..<8 {
                if checksum & 1 == 1 {
                    checksum = 0xedb88320 ^ (checksum >> 1)
                } else {
                    checksum >>= 1
                }
            }

            return checksum
        }
    }()

    static func algorithms(from arguments: [String: String]) throws -> [String] {
        guard let rawValue = AnsightFileSystemSandbox.string(arguments, key: "algorithms") else {
            return ["sha256"]
        }

        let requested = rawValue
            .split(separator: ",")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
            .filter { !$0.isEmpty }

        guard !requested.isEmpty else {
            return ["sha256"]
        }

        if requested.contains("all") {
            return allAlgorithms
        }

        for algorithm in requested where !allAlgorithms.contains(algorithm) {
            throw AnsightFileSystemToolError.invalidArgument(
                "The checksum algorithm '\(algorithm)' is not supported."
            )
        }

        var unique: [String] = []
        for algorithm in requested where !unique.contains(algorithm) {
            unique.append(algorithm)
        }

        return unique
    }

    static func checksums(data: Data, algorithms: [String]) throws -> [AnsightFileSystemChecksum] {
        try algorithms.map { algorithm in
            let checksum: String
            switch algorithm {
            case "md5":
                checksum = hex(Insecure.MD5.hash(data: data))
            case "sha1":
                checksum = hex(Insecure.SHA1.hash(data: data))
            case "sha256":
                checksum = hex(SHA256.hash(data: data))
            case "sha384":
                checksum = hex(SHA384.hash(data: data))
            case "sha512":
                checksum = hex(SHA512.hash(data: data))
            case "crc32":
                checksum = String(format: "%08x", crc32(data: data))
            default:
                throw AnsightFileSystemToolError.invalidArgument(
                    "The checksum algorithm '\(algorithm)' is not supported."
                )
            }

            return AnsightFileSystemChecksum(algorithm: algorithm, checksum: checksum, encoding: "hex")
        }
    }

    private static func hex<D: Sequence>(_ digest: D) -> String where D.Element == UInt8 {
        digest.map { String(format: "%02x", $0) }.joined()
    }

    private static func crc32(data: Data) -> UInt32 {
        var checksum: UInt32 = 0xffff_ffff
        for byte in data {
            let index = Int((checksum ^ UInt32(byte)) & 0xff)
            checksum = crc32Table[index] ^ (checksum >> 8)
        }

        return checksum ^ 0xffff_ffff
    }
}
