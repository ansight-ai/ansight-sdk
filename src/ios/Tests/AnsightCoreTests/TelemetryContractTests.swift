import XCTest
@testable import AnsightCore

final class TelemetryContractTests: XCTestCase {
    func testFractionalMetricsAndSpanOutcomes() throws {
        let runtime = AnsightRuntime.shared
        try runtime.initialize(options: AnsightOptions(
            additionalChannels: [AnsightChannel(id: 64, name: "Frame", unit: "ms", group: "Render")],
            defaultMemoryChannels: .none, enableFramesPerSecond: false, hostAutoProbe: .disabledDefault))
        try runtime.activate()
        defer { runtime.deactivate() }
        try runtime.metric(16.67, channel: 64)
        try runtime.metric(Int64(7), channel: 64)
        XCTAssertThrowsError(try runtime.metric(Double.nan, channel: 64))
        XCTAssertEqual(runtime.recordedMetrics().suffix(2).map(\.value), [16.67, 7])
        let encoded = try JSONEncoder().encode(RecordedMetric(value: 0.125, channel: 64))
        XCTAssertEqual(try JSONDecoder().decode(RecordedMetric.self, from: encoded).value, 0.125)
        let span = try runtime.beginSpan("Load", channel: 64, parentSpanId: "parent")
        try span.end(status: "error")
        try span.end()
        let events = runtime.recordedEvents().filter { $0.type == .span && $0.label == "Load" }
        XCTAssertEqual(events.count, 2)
        let details = try XCTUnwrap(events.last?.details).data(using: .utf8)!
        let fields = try JSONSerialization.jsonObject(with: details) as! [String: Any]
        XCTAssertEqual(fields["spanId"] as? String, span.id)
        XCTAssertEqual(fields["parentSpanId"] as? String, "parent")
        XCTAssertEqual(fields["status"] as? String, "error")
        XCTAssertGreaterThanOrEqual(fields["durationMs"] as! Double, 0)
    }
}
