import XCTest
@testable import AnsightCore
@testable import AnsightToolsFileSystem

final class FileSystemToolTests: XCTestCase {
    func testFileSystemToolsRoundTripViaToolProtocol() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        try FileManager.default.createDirectory(at: root.appendingPathComponent("sub"), withIntermediateDirectories: true)
        try "hello world".data(using: .utf8)?.write(to: root.appendingPathComponent("sub/hello.txt"))

        let bridge = bridge(options: options(root: root), guardPolicy: .fullAccess)

        let listEnvelope = try call(
            bridge,
            id: "files_list",
            toolId: AnsightFileSystemToolIds.listDirectory,
            arguments: [
                "root": "test",
                "path": "sub",
            ]
        )
        let listResult = try resultPayload(listEnvelope)
        XCTAssertEqual(listResult["rootAlias"], .string("test"))
        guard case .array(let entries)? = listResult["entries"] else {
            return XCTFail("Expected directory entries.")
        }
        XCTAssertTrue(entries.contains { entry in
            guard case .object(let object) = entry else {
                return false
            }

            return object["name"] == .string("hello.txt") &&
                object["kind"] == .string("file")
        })

        let readEnvelope = try call(
            bridge,
            id: "files_read",
            toolId: AnsightFileSystemToolIds.readFile,
            arguments: [
                "root": "test",
                "path": "sub/hello.txt",
                "encoding": "utf8",
            ]
        )
        let readResult = try resultPayload(readEnvelope)
        XCTAssertEqual(readResult["contentType"], .string("text"))
        XCTAssertEqual(readResult["encoding"], .string("utf-8"))
        XCTAssertEqual(readResult["text"], .string("hello world"))

        let checksumEnvelope = try call(
            bridge,
            id: "files_checksum",
            toolId: AnsightFileSystemToolIds.getFileChecksum,
            arguments: [
                "root": "test",
                "path": "sub/hello.txt",
                "algorithms": "sha256,crc32",
            ]
        )
        let checksumResult = try resultPayload(checksumEnvelope)
        guard case .array(let checksums)? = checksumResult["checksums"] else {
            return XCTFail("Expected checksum entries.")
        }
        XCTAssertTrue(checksums.contains(.object([
            "algorithm": .string("sha256"),
            "checksum": .string("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9"),
            "encoding": .string("hex"),
        ])))
        XCTAssertTrue(checksums.contains(.object([
            "algorithm": .string("crc32"),
            "checksum": .string("0d4a1185"),
            "encoding": .string("hex"),
        ])))

        let firstDownloadEnvelope = try call(
            bridge,
            id: "files_download_1",
            toolId: AnsightFileSystemToolIds.downloadFile,
            arguments: [
                "root": "test",
                "path": "sub/hello.txt",
                "maxBytes": "5",
                "encoding": "utf8",
            ]
        )
        let firstDownload = try resultPayload(firstDownloadEnvelope)
        XCTAssertEqual(firstDownload["text"], .string("hello"))
        XCTAssertEqual(firstDownload["hasMore"], .bool(true))
        XCTAssertEqual(firstDownload["nextOffsetBytes"], .integer(5))

        let version = try string(firstDownload, key: "version")
        let secondDownloadEnvelope = try call(
            bridge,
            id: "files_download_2",
            toolId: AnsightFileSystemToolIds.downloadFile,
            arguments: [
                "root": "test",
                "path": "sub/hello.txt",
                "offsetBytes": "5",
                "maxBytes": "64",
                "encoding": "utf8",
                "expectedVersion": version,
            ]
        )
        let secondDownload = try resultPayload(secondDownloadEnvelope)
        XCTAssertEqual(secondDownload["text"], .string(" world"))
        XCTAssertEqual(secondDownload["hasMore"], .bool(false))

        let pushEnvelope = try call(
            bridge,
            id: "files_push",
            toolId: AnsightFileSystemToolIds.pushFile,
            arguments: [
                "root": "test",
                "directoryPath": "out",
                "fileName": "new.txt",
                "text": "pushed",
                "createDirectory": "true",
            ]
        )
        let pushResult = try resultPayload(pushEnvelope)
        XCTAssertEqual(pushResult["operation"], .string("created"))
        XCTAssertEqual(pushResult["createdDirectory"], .bool(true))

        let copyEnvelope = try call(
            bridge,
            id: "files_copy",
            toolId: AnsightFileSystemToolIds.copyFile,
            arguments: [
                "root": "test",
                "sourcePath": "out/new.txt",
                "destinationPath": "out/copied.txt",
            ]
        )
        XCTAssertEqual(try resultPayload(copyEnvelope)["operation"], .string("copied"))
        XCTAssertTrue(FileManager.default.fileExists(atPath: root.appendingPathComponent("out/copied.txt").path))

        let moveEnvelope = try call(
            bridge,
            id: "files_move",
            toolId: AnsightFileSystemToolIds.moveFile,
            arguments: [
                "root": "test",
                "sourcePath": "out/copied.txt",
                "destinationPath": "out/moved.txt",
            ]
        )
        XCTAssertEqual(try resultPayload(moveEnvelope)["operation"], .string("moved"))
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("out/copied.txt").path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: root.appendingPathComponent("out/moved.txt").path))

        let deleteEnvelope = try call(
            bridge,
            id: "files_delete",
            toolId: AnsightFileSystemToolIds.deleteFile,
            arguments: [
                "root": "test",
                "path": "out/moved.txt",
            ]
        )
        XCTAssertEqual(try resultPayload(deleteEnvelope)["deleted"], .bool(true))
        XCTAssertFalse(FileManager.default.fileExists(atPath: root.appendingPathComponent("out/moved.txt").path))
    }

    func testFileSystemCatalogIncludesSecurityMetadata() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let envelope = try queryCatalog(bridge(options: options(root: root), guardPolicy: .fullAccess))
        XCTAssertEqual(envelope.type, "tool.catalog")
        guard case .object(let payload) = envelope.payload,
              case .array(let tools)? = payload["tools"] else {
            return XCTFail("Expected catalog tools.")
        }

        let deleteTool = tools.compactMap { tool -> [String: JSONValue]? in
            guard case .object(let object) = tool,
                  object["id"] == .string(AnsightFileSystemToolIds.deleteFile) else {
                return nil
            }

            return object
        }.first

        guard let deleteTool,
              case .object(let security)? = deleteTool["security"],
              case .array(let implications)? = security["implications"] else {
            return XCTFail("Expected delete-file security metadata.")
        }

        XCTAssertEqual(security["level"], .string("Critical"))
        XCTAssertTrue(implications.contains(.string("deletes_app_data")))
        XCTAssertTrue(implications.contains(.string("accesses_file_system")))
    }

    func testReadOnlyGuardDeniesFileWrites() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let envelope = try call(
            bridge(options: options(root: root), guardPolicy: .readOnly),
            id: "files_write_denied",
            toolId: AnsightFileSystemToolIds.pushFile,
            arguments: [
                "root": "test",
                "directoryPath": ".",
                "fileName": "denied.txt",
                "text": "denied",
            ]
        )

        XCTAssertEqual(envelope.type, "tool.error")
        XCTAssertEqual(errorCode(envelope), "tool_execution_denied")
    }

    func testBinaryDownloadRequiresLiveTransport() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }
        try "binary".data(using: .utf8)?.write(to: root.appendingPathComponent("file.bin"))

        let envelope = try call(
            bridge(options: options(root: root), guardPolicy: .fullAccess),
            id: "files_binary",
            toolId: AnsightFileSystemToolIds.beginBinaryDownload,
            arguments: [
                "root": "test",
                "path": "file.bin",
            ]
        )

        XCTAssertEqual(envelope.type, "tool.error")
        XCTAssertEqual(errorCode(envelope), "filesystem_binary_download_unavailable")
    }

    func testBinaryTransferWireProtocolFramesSustainedLargePayload() throws {
        let transferId = try XCTUnwrap(UUID(uuidString: "01234567-89AB-CDEF-0123-456789ABCDEF"))
        let sourcePayload = Data((0..<150_000).map { UInt8($0 % 251) })
        let chunkBytes = 64 * 1024
        var frames: [Data] = []
        var sequence: Int32 = 0
        var offsetBytes: Int64 = 0

        while offsetBytes < sourcePayload.count {
            let remainingBytes = sourcePayload.count - Int(offsetBytes)
            let byteCount = min(chunkBytes, remainingBytes)
            let payload = sourcePayload.subdata(in: Int(offsetBytes)..<Int(offsetBytes) + byteCount)
            frames.append(
                PairingFileTransferWireProtocol.createFrame(
                    transferId: transferId,
                    frameType: .chunk,
                    sequence: sequence,
                    offsetBytes: offsetBytes,
                    payload: payload
                )
            )

            sequence += 1
            offsetBytes += Int64(byteCount)
        }

        frames.append(
            PairingFileTransferWireProtocol.createFrame(
                transferId: transferId,
                frameType: .complete,
                sequence: sequence,
                offsetBytes: offsetBytes,
                payload: Data()
            )
        )

        let expectedPayloadCounts = [65_536, 65_536, 18_928, 0]
        let expectedOffsets = [0, 65_536, 131_072, 150_000]
        let transferIdHex = transferId.uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        var reconstructedPayload = Data()

        XCTAssertEqual(frames.count, expectedPayloadCounts.count)
        for (index, frame) in frames.enumerated() {
            let isCompleteFrame = index == frames.count - 1
            XCTAssertEqual(frame.count, PairingFileTransferWireProtocol.headerSize + expectedPayloadCounts[index])
            XCTAssertEqual(Array(frame[0..<4]), Array(PairingFileTransferWireProtocol.magic.utf8))
            XCTAssertEqual(frame[4], PairingFileTransferWireProtocol.version)
            XCTAssertEqual(
                frame[5],
                isCompleteFrame
                    ? PairingFileTransferFrameType.complete.rawValue
                    : PairingFileTransferFrameType.chunk.rawValue
            )
            XCTAssertEqual(String(decoding: frame[8..<40], as: UTF8.self), transferIdHex)
            XCTAssertEqual(readInt32(frame, at: 40), Int32(index))
            XCTAssertEqual(readInt64(frame, at: 44), Int64(expectedOffsets[index]))
            XCTAssertEqual(readInt32(frame, at: 52), Int32(expectedPayloadCounts[index]))

            if !isCompleteFrame {
                reconstructedPayload.append(frame.subdata(in: PairingFileTransferWireProtocol.headerSize..<frame.count))
            }
        }

        XCTAssertEqual(reconstructedPayload, sourcePayload)
    }

    func testPendingBinaryTransferClampsChunkSizeToRuntimeBounds() {
        let transferId = UUID()
        let tinyChunkTransfer = AnsightPendingBinaryTransfer(
            transferId: transferId,
            data: Data(),
            chunkBytes: 0,
            description: "tiny"
        )
        let defaultChunkTransfer = AnsightPendingBinaryTransfer(
            transferId: transferId,
            data: Data(),
            chunkBytes: 64 * 1_024,
            description: "default"
        )
        let oversizedChunkTransfer = AnsightPendingBinaryTransfer(
            transferId: transferId,
            data: Data(),
            chunkBytes: 2 * 1_024 * 1_024,
            description: "oversized"
        )

        XCTAssertEqual(tinyChunkTransfer.chunkBytes, 1)
        XCTAssertEqual(defaultChunkTransfer.chunkBytes, 64 * 1_024)
        XCTAssertEqual(oversizedChunkTransfer.chunkBytes, 1_024 * 1_024)
    }

    private func options(root: URL) -> AnsightFileSystemToolsOptions {
        AnsightFileSystemToolsOptions(additionalRoots: [
            AnsightFileSystemRoot(alias: "test", path: root.path),
        ])
    }

    private func makeTemporaryRoot() throws -> URL {
        let root = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("ansight-files-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        return root
    }

    private func bridge(options: AnsightFileSystemToolsOptions, guardPolicy: AnsightToolGuard) -> AnsightToolProtocolBridge {
        let tools = AnsightFileSystemTools.tools(options: options)
        let registry = Dictionary(
            uniqueKeysWithValues: tools.map { tool in
                (
                    AnsightToolProtocolBridge.normalizedToolId(tool.descriptor.id),
                    RegisteredTool(
                        descriptor: tool.descriptor,
                        execute: { arguments in
                            try tool.execute(arguments: arguments)
                        }
                    )
                )
            }
        )

        return AnsightToolProtocolBridge(registry: registry, guardPolicy: guardPolicy)
    }

    private func queryCatalog(_ bridge: AnsightToolProtocolBridge) throws -> AnsightToolProtocolEnvelope {
        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.query","id":"files_catalog","capability":"tool.exec","payload":{}}
            """
        )
        return try decodeEnvelope(responseJson)
    }

    private func call(
        _ bridge: AnsightToolProtocolBridge,
        id: String,
        toolId: String,
        arguments: [String: String]
    ) throws -> AnsightToolProtocolEnvelope {
        let envelope = AnsightToolProtocolEnvelope(
            type: "tool.call",
            id: id,
            sessionId: "files_session",
            payload: .object([
                "toolId": .string(toolId),
                "arguments": .object(from: arguments),
            ])
        )
        let data = try JSONEncoder().encode(envelope)
        let request = try XCTUnwrap(String(data: data, encoding: .utf8))
        let responseJson = try bridge.handleIfSupported(request)
        return try decodeEnvelope(responseJson)
    }

    private func decodeEnvelope(_ json: String?) throws -> AnsightToolProtocolEnvelope {
        let json = try XCTUnwrap(json)
        let data = try XCTUnwrap(json.data(using: .utf8))
        return try JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: data)
    }

    private func resultPayload(_ envelope: AnsightToolProtocolEnvelope) throws -> [String: JSONValue] {
        guard case .object(let payload) = envelope.payload,
              case .object(let result)? = payload["result"] else {
            XCTFail("Expected tool result payload.")
            return [:]
        }

        return result
    }

    private func string(_ object: [String: JSONValue], key: String) throws -> String {
        guard case .string(let value)? = object[key] else {
            throw XCTSkip("Expected string value for \(key).")
        }

        return value
    }

    private func errorCode(_ envelope: AnsightToolProtocolEnvelope) -> String? {
        guard case .object(let payload) = envelope.payload,
              case .string(let code)? = payload["code"] else {
            return nil
        }

        return code
    }

    private func readInt32(_ data: Data, at index: Int) -> Int32 {
        var value: Int32 = 0
        _ = withUnsafeMutableBytes(of: &value) { buffer in
            data.copyBytes(to: buffer, from: index..<index + MemoryLayout<Int32>.size)
        }
        return Int32(littleEndian: value)
    }

    private func readInt64(_ data: Data, at index: Int) -> Int64 {
        var value: Int64 = 0
        _ = withUnsafeMutableBytes(of: &value) { buffer in
            data.copyBytes(to: buffer, from: index..<index + MemoryLayout<Int64>.size)
        }
        return Int64(littleEndian: value)
    }
}
