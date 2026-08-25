import Foundation
import zlib

internal enum AnsightToolProtocolPayloadEncoding {
    private static let encodingPropertyName = "$ansightEncoding"
    private static let gzipBase64JSONEncoding = "gzip-base64-json"
    private static let compressionThresholdBytes = 32 * 1024

    static func encodeIfBeneficial(_ payload: JSONValue) -> JSONValue {
        guard let sourceData = try? payload.jsonData(),
              sourceData.count >= compressionThresholdBytes,
              let compressedData = try? gzip(sourceData)
        else {
            return payload
        }

        let encodedPayload = JSONValue.object([
            encodingPropertyName: .string(gzipBase64JSONEncoding),
            "contentType": .string("application/json"),
            "originalByteCount": .integer(Int64(sourceData.count)),
            "compressedByteCount": .integer(Int64(compressedData.count)),
            "data": .string(compressedData.base64EncodedString()),
        ])
        guard let encodedPayloadData = try? encodedPayload.jsonData(),
              encodedPayloadData.count < sourceData.count else {
            return payload
        }
        return encodedPayload
    }

    static func decodeIfNeeded(_ payload: JSONValue) -> JSONValue? {
        guard case .object(let object) = payload,
              case .string(let encoding)? = object[encodingPropertyName]
        else {
            return payload
        }

        guard encoding == gzipBase64JSONEncoding,
              case .string(let encodedData)? = object["data"],
              let compressedData = Data(base64Encoded: encodedData),
              let sourceData = try? gunzip(compressedData)
        else {
            return nil
        }

        return try? JSONDecoder().decode(JSONValue.self, from: sourceData)
    }

    private static func gzip(_ data: Data) throws -> Data {
        try data.withUnsafeBytes { sourceBuffer in
            var stream = z_stream()
            let initStatus = deflateInit2_(
                &stream,
                Z_DEFAULT_COMPRESSION,
                Z_DEFLATED,
                MAX_WBITS + 16,
                8,
                Z_DEFAULT_STRATEGY,
                ZLIB_VERSION,
                Int32(MemoryLayout<z_stream>.size)
            )
            guard initStatus == Z_OK else {
                throw CodecError.initializationFailed
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
            var status = Int32(Z_OK)
            repeat {
                status = chunk.withUnsafeMutableBytes { chunkBuffer in
                    let destination = chunkBuffer.bindMemory(to: Bytef.self)
                    stream.next_out = destination.baseAddress
                    stream.avail_out = uInt(chunkSize)
                    return deflate(&stream, stream.avail_in == 0 ? Z_FINISH : Z_NO_FLUSH)
                }

                guard status == Z_OK || status == Z_STREAM_END else {
                    throw CodecError.compressionFailed
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
                throw CodecError.initializationFailed
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
            var status = Int32(Z_OK)
            repeat {
                status = chunk.withUnsafeMutableBytes { chunkBuffer in
                    let destination = chunkBuffer.bindMemory(to: Bytef.self)
                    stream.next_out = destination.baseAddress
                    stream.avail_out = uInt(chunkSize)
                    return inflate(&stream, Z_NO_FLUSH)
                }

                guard status == Z_OK || status == Z_STREAM_END else {
                    throw CodecError.decompressionFailed
                }

                let written = chunkSize - Int(stream.avail_out)
                if written > 0 {
                    output.append(contentsOf: chunk.prefix(written))
                }
            } while status != Z_STREAM_END

            return output
        }
    }

    private enum CodecError: Error {
        case initializationFailed
        case compressionFailed
        case decompressionFailed
    }
}
