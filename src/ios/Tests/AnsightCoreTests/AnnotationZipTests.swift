import Foundation
import XCTest
@testable import AnsightCore

final class AnnotationZipTests: XCTestCase {
    func testAnnotationBundleIsReadableByStandardZipReader() throws {
        let manifest = Data("{\"schema\":\"ansight.annotation.bundle.v1\"}".utf8)
        let bundle = try AnnotationZip.make([
            ("evidence/screenshot.jpg", Data([0xff, 0xd8, 0xff, 0xd9])),
            ("manifest.json", manifest)
        ])
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".ansightannotation")
        try bundle.write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        process.arguments = ["-p", url.path, "manifest.json"]
        let output = Pipe()
        process.standardOutput = output
        process.standardError = Pipe()
        try process.run()
        process.waitUntilExit()

        XCTAssertEqual(process.terminationStatus, 0)
        XCTAssertEqual(output.fileHandleForReading.readDataToEndOfFile(), manifest)
    }
}
