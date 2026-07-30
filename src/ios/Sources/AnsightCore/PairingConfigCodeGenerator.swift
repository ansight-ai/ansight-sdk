import Foundation
import zlib

public enum PairingConfigCodeGenerator {
    public static let formatPrefix = "ans2"

    public static func serialize(_ document: PairingConfigDocument) throws -> String {
        let jsonData = try JSONEncoder.ansightEncoder.encode(document)
        let compressedData = try gzip(jsonData)
        return "\(formatPrefix):\(base64UrlEncode(compressedData))"
    }

    public static func tryParse(_ payload: String) -> PairingConfigDocument? {
        let normalizedPayload = payload.trimmingCharacters(in: .whitespacesAndNewlines)
        guard normalizedPayload.hasPrefix("\(formatPrefix):") else {
            return nil
        }

        let encodedPayload = normalizedPayload.dropFirst(formatPrefix.count + 1)
        guard let compressedData = base64UrlDecode(String(encodedPayload)),
              let jsonData = try? gunzip(compressedData),
              let document = try? JSONDecoder.ansightDecoder.decode(PairingConfigDocument.self, from: jsonData),
              document.schema == PairingConfigDocument.schemaName
        else {
            return nil
        }

        return document
    }

    private static func base64UrlEncode(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "=", with: "")
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
    }

    private static func base64UrlDecode(_ value: String) -> Data? {
        var normalized = value
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "-", with: "+")
            .replacingOccurrences(of: "_", with: "/")

        switch normalized.count % 4 {
        case 0:
            break
        case 2:
            normalized.append("==")
        case 3:
            normalized.append("=")
        default:
            return nil
        }

        return Data(base64Encoded: normalized)
    }

    private static func gzip(_ data: Data) throws -> Data {
        try data.withUnsafeBytes { sourceBuffer in
            var stream = z_stream()
            let initStatus = deflateInit2_(
                &stream,
                Z_BEST_COMPRESSION,
                Z_DEFLATED,
                MAX_WBITS + 16,
                8,
                Z_DEFAULT_STRATEGY,
                ZLIB_VERSION,
                Int32(MemoryLayout<z_stream>.size)
            )
            guard initStatus == Z_OK else {
                throw PairingDocumentError.invalidDocument("Failed to initialize gzip compression.")
            }
            defer {
                deflateEnd(&stream)
            }

            let source = sourceBuffer.bindMemory(to: Bytef.self)
            stream.next_in = UnsafeMutablePointer<Bytef>(mutating: source.baseAddress)
            stream.avail_in = uInt(sourceBuffer.count)

            var output = Data()
            let chunkSize = 16 * 1024
            var chunk = [UInt8](repeating: 0, count: chunkSize)
            var status: Int32 = Z_OK
            repeat {
                status = chunk.withUnsafeMutableBytes { chunkBuffer in
                    let destination = chunkBuffer.bindMemory(to: Bytef.self)
                    stream.next_out = destination.baseAddress
                    stream.avail_out = uInt(chunkSize)
                    return deflate(&stream, stream.avail_in == 0 ? Z_FINISH : Z_NO_FLUSH)
                }

                guard status == Z_OK || status == Z_STREAM_END else {
                    throw PairingDocumentError.invalidDocument("Failed to gzip enrollment invite.")
                }

                let written = chunkSize - Int(stream.avail_out)
                if written > 0 {
                    output.append(contentsOf: chunk.prefix(written))
                }
            } while status != Z_STREAM_END

            return output
        }
    }

    private static func gunzip(_ data: Data) throws -> Data {
        try data.withUnsafeBytes { sourceBuffer in
            var stream = z_stream()
            let initStatus = inflateInit2_(
                &stream,
                MAX_WBITS + 32,
                ZLIB_VERSION,
                Int32(MemoryLayout<z_stream>.size)
            )
            guard initStatus == Z_OK else {
                throw PairingDocumentError.invalidDocument("Failed to initialize gzip decompression.")
            }
            defer {
                inflateEnd(&stream)
            }

            let source = sourceBuffer.bindMemory(to: Bytef.self)
            stream.next_in = UnsafeMutablePointer<Bytef>(mutating: source.baseAddress)
            stream.avail_in = uInt(sourceBuffer.count)

            var output = Data()
            let chunkSize = 16 * 1024
            var chunk = [UInt8](repeating: 0, count: chunkSize)
            var status: Int32 = Z_OK
            repeat {
                status = chunk.withUnsafeMutableBytes { chunkBuffer in
                    let destination = chunkBuffer.bindMemory(to: Bytef.self)
                    stream.next_out = destination.baseAddress
                    stream.avail_out = uInt(chunkSize)
                    return inflate(&stream, Z_NO_FLUSH)
                }

                guard status == Z_OK || status == Z_STREAM_END else {
                    throw PairingDocumentError.invalidDocument("Failed to decompress enrollment QR code.")
                }

                let written = chunkSize - Int(stream.avail_out)
                if written > 0 {
                    output.append(contentsOf: chunk.prefix(written))
                }
            } while status != Z_STREAM_END

            return output
        }
    }
}
