import Foundation
import XCTest
@testable import AnsightCore

final class ConnectivityTests: XCTestCase {
    func testOptionsAreOptInAndRoundTrip() throws {
        XCTAssertNil(AnsightOptions().connectivityCapture)
        var options = AnsightOptions()
        options.connectivityCapture = .init(minimumSampleIntervalMilliseconds: 1)
        let decoded = try JSONDecoder().decode(AnsightOptions.self, from: JSONEncoder().encode(options))
        XCTAssertEqual(try decoded.validated().connectivityCapture?.minimumSampleIntervalMilliseconds, 1_000)
    }

    func testSuspendDrainsObservationsAndResumeStartsNewSnapshot() throws {
        let output = Observations()
        let monitor = AnsightConnectivityMonitor(options: .init()) { _, json, _ in output.append(json) }
        monitor.resume()
        monitor.suspend(reason: "app-backgrounded")
        monitor.resume()
        monitor.suspend(reason: "capture-stopped")
        let values = try output.read().map { try JSONSerialization.jsonObject(with: Data($0.utf8)) as! [String: Any] }
        XCTAssertEqual(values.filter { $0["kind"] as? String == "snapshot" }.count, 2)
        XCTAssertEqual(values.filter { $0["kind"] as? String == "gap" }.count, 2)
        XCTAssertEqual(values.last?["reason"] as? String, "capture-stopped")
        XCTAssertEqual(Set(values.compactMap { $0["id"] as? String }).count, values.count)
        XCTAssertEqual(values.compactMap { $0["sequence"] as? Int }, Array(1...values.count))
        for value in values where value["kind"] as? String == "gap" {
            XCTAssertNil(value["quality"])
            XCTAssertEqual((value["snapshot"] as? [String: Any])?["availability"] as? String, "unknown")
        }
    }

    private final class Observations: @unchecked Sendable {
        private let lock = NSLock()
        private var values: [String] = []
        func append(_ json: String) { lock.lock(); defer { lock.unlock() }; values.append(json) }
        func read() -> [String] { lock.lock(); defer { lock.unlock() }; return values }
    }
}
