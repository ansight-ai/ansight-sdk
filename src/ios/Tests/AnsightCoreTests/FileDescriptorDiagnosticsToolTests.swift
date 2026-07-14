import Foundation
import XCTest
@testable import AnsightCore
@testable import AnsightToolsFileDescriptorDiagnostics

final class FileDescriptorDiagnosticsToolTests: XCTestCase {
    func testListOpenFiltersKindAndLimitsResults() throws {
        let collector = StubFileDescriptorCollector(descriptors: [
            descriptor(3, kind: .regularFile, target: "/tmp/one.db"),
            descriptor(4, kind: .socket, target: nil),
            descriptor(5, kind: .regularFile, target: "/tmp/two.db"),
        ])
        let tool = ListOpenFileDescriptorsTool(options: .default, collector: collector)

        let result = try tool.execute(arguments: ["kind": "regular_file", "maxEntries": "1"])
        let payload = try XCTUnwrap(resultObject(result))

        XCTAssertTrue(result.success)
        XCTAssertEqual(payload["count"], .integer(3))
        XCTAssertEqual(payload["matchedCount"], .integer(2))
        XCTAssertEqual(payload["returnedCount"], .integer(1))
        XCTAssertEqual(payload["truncated"], .bool(true))
    }

    func testCountOpenReturnsCountWithoutDescriptorRecords() throws {
        let collector = StubFileDescriptorCollector(descriptors: [
            descriptor(0, kind: .characterDevice, target: "/dev/null"),
            descriptor(7, kind: .pipe, target: nil),
        ])
        let tool = CountOpenFileDescriptorsTool(options: .default, collector: collector)

        let result = try tool.execute(arguments: [:])
        let payload = try XCTUnwrap(resultObject(result))

        XCTAssertEqual(payload["count"], .integer(2))
        XCTAssertEqual(Set(payload.keys), ["count"])
    }

    func testInspectReportsClosedDescriptor() throws {
        let tool = InspectFileDescriptorTool(
            options: .default,
            collector: StubFileDescriptorCollector(descriptors: [])
        )

        let result = try tool.execute(arguments: ["descriptor": "42"])

        XCTAssertFalse(result.success)
        XCTAssertEqual(result.errorCode, "file_descriptor_not_open")
    }

    func testCountOpenRejectsIncompleteScanInsteadOfReturningAnUndercount() throws {
        let tool = CountOpenFileDescriptorsTool(
            options: .default,
            collector: StubFileDescriptorCollector(descriptors: [], scanComplete: false)
        )

        let result = try tool.execute(arguments: [:])

        XCTAssertFalse(result.success)
        XCTAssertEqual(result.errorCode, "file_descriptor_scan_incomplete")
    }

    func testUsageReportsLimitsAndUtilization() throws {
        let collector = StubFileDescriptorCollector(
            descriptors: [
                descriptor(1, kind: .characterDevice, target: nil),
                descriptor(2, kind: .characterDevice, target: nil),
            ],
            limits: AnsightFileDescriptorLimits(softLimit: 8, hardLimit: 16, hardLimitUnlimited: false)
        )
        let tool = GetFileDescriptorUsageTool(options: .default, collector: collector)

        let result = try tool.execute(arguments: [:])
        let payload = try XCTUnwrap(resultObject(result))

        XCTAssertEqual(payload["openCount"], .integer(2))
        XCTAssertEqual(payload["availableBeforeSoftLimit"], .integer(6))
        XCTAssertEqual(payload["utilizationPercent"], .number(25))
    }

    func testSystemCollectorInspectsOpenTemporaryFile() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try Data("ansight".utf8).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }

        let info = try AnsightSystemFileDescriptorCollector().inspect(
            descriptor: Int(handle.fileDescriptor),
            includeTarget: true
        )

        XCTAssertEqual(info?.kind, .regularFile)
        XCTAssertEqual(info?.target.map { URL(fileURLWithPath: $0).lastPathComponent }, url.lastPathComponent)
        XCTAssertEqual(info?.accessMode, "read_only")
    }

    private func descriptor(
        _ number: Int,
        kind: AnsightFileDescriptorKind,
        target: String?
    ) -> AnsightFileDescriptorInfo {
        AnsightFileDescriptorInfo(
            descriptor: number,
            kind: kind,
            target: target,
            accessMode: "read_only",
            closeOnExec: true,
            descriptorFlags: 1,
            statusFlags: 0,
            positionBytes: 0,
            inode: UInt64(number + 100)
        )
    }

    private func resultObject(_ result: AnsightToolExecutionResult) -> [String: JSONValue]? {
        guard case .object(let object)? = result.result else {
            return nil
        }
        return object
    }
}

private struct StubFileDescriptorCollector: AnsightFileDescriptorCollecting {
    let descriptors: [AnsightFileDescriptorInfo]
    let configuredLimits: AnsightFileDescriptorLimits
    let scanComplete: Bool

    init(
        descriptors: [AnsightFileDescriptorInfo],
        limits: AnsightFileDescriptorLimits = AnsightFileDescriptorLimits(
            softLimit: 256,
            hardLimit: nil,
            hardLimitUnlimited: true
        ),
        scanComplete: Bool = true
    ) {
        self.descriptors = descriptors
        self.configuredLimits = limits
        self.scanComplete = scanComplete
    }

    func snapshot(options: AnsightFileDescriptorDiagnosticsOptions) throws -> AnsightFileDescriptorSnapshot {
        AnsightFileDescriptorSnapshot(
            descriptors: descriptors,
            limits: configuredLimits,
            scanComplete: scanComplete,
            scannedDescriptorLimit: Int(configuredLimits.softLimit ?? 0)
        )
    }

    func inspect(descriptor: Int, includeTarget: Bool) throws -> AnsightFileDescriptorInfo? {
        descriptors.first { $0.descriptor == descriptor }
    }

    func count(options: AnsightFileDescriptorDiagnosticsOptions) throws -> AnsightFileDescriptorCountSnapshot {
        AnsightFileDescriptorCountSnapshot(
            count: descriptors.count,
            limits: configuredLimits,
            scanComplete: scanComplete,
            scannedDescriptorLimit: Int(configuredLimits.softLimit ?? 0)
        )
    }

    func limits() throws -> AnsightFileDescriptorLimits {
        configuredLimits
    }
}
