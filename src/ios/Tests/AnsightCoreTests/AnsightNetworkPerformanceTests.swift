import Foundation
import XCTest
@testable import AnsightCore

final class AnsightNetworkPerformanceTests: XCTestCase {
    func testCapturesNetworkPhasesAndTransferredBytes() throws {
        let transaction = PerformanceTransactionStub()
        let performance = try XCTUnwrap(AnsightNetworkPerformance.capture(transaction, includeBodySizes: true))
        XCTAssertEqual(try XCTUnwrap(performance.timeToFirstByteMilliseconds), 250, accuracy: 0.01)
        XCTAssertEqual(try XCTUnwrap(performance.downloadDurationMilliseconds), 2_000, accuracy: 0.01)
        XCTAssertEqual(try XCTUnwrap(performance.uploadDurationMilliseconds), 100, accuracy: 0.01)
        XCTAssertEqual(performance.downloadedBytes, 4_000_000)
        XCTAssertEqual(performance.uploadedBytes, 10_000)
        XCTAssertEqual(try XCTUnwrap(performance.dnsMilliseconds), 10, accuracy: 0.01)
        XCTAssertEqual(try XCTUnwrap(performance.connectMilliseconds), 60, accuracy: 0.01)
        XCTAssertEqual(try XCTUnwrap(performance.tlsMilliseconds), 40, accuracy: 0.01)
    }

    func testMissingPhasesAndDisabledSizesRemainUnknown() throws {
        let transaction = PerformanceTransactionStub()
        transaction.reused = true
        transaction.finished = false
        let performance = try XCTUnwrap(AnsightNetworkPerformance.capture(transaction, includeBodySizes: false))
        XCTAssertTrue(try XCTUnwrap(performance.reusedConnection))
        XCTAssertNil(performance.dnsMilliseconds)
        XCTAssertNil(performance.connectMilliseconds)
        XCTAssertNil(performance.tlsMilliseconds)
        XCTAssertNil(performance.downloadDurationMilliseconds)
        XCTAssertNil(performance.downloadedBytes)
        XCTAssertNil(performance.uploadedBytes)
        XCTAssertNotNil(performance.timeToFirstByteMilliseconds)
    }

    func testCachedResponsesAreNotNetworkMeasurements() {
        let transaction = PerformanceTransactionStub()
        transaction.fetchType = .localCache
        XCTAssertNil(AnsightNetworkPerformance.capture(transaction, includeBodySizes: true))
    }

    func testPerformanceSurvivesSerializationAndSanitization() throws {
        let performance = AnsightNetworkPerformance.capture(PerformanceTransactionStub(), includeBodySizes: true)
        let request = AnsightNetworkRequest(id: "measured", source: "apple.nsurlprotocol",
            startedAtUtc: "2026-10-11T00:00:00Z", completedAtUtc: "2026-10-11T00:00:03Z",
            durationMilliseconds: 3_000, performance: performance, method: "GET", url: "https://example.test")
        let decoded = try JSONDecoder().decode(AnsightNetworkRequest.self, from: JSONEncoder().encode(request))
        XCTAssertEqual(AnsightNetworkRequestSanitizer.sanitize(decoded).performance, performance)
        XCTAssertEqual(decoded.withRedactSensitiveData(false).performance, performance)
        var legacy = try XCTUnwrap(JSONSerialization.jsonObject(with: JSONEncoder().encode(request)) as? [String: Any])
        legacy.removeValue(forKey: "performance")
        let legacyRequest = try JSONDecoder().decode(AnsightNetworkRequest.self, from: JSONSerialization.data(withJSONObject: legacy))
        XCTAssertNil(legacyRequest.performance)
    }

    func testInvalidValuesAreRemovedWithoutChangingZero() {
        var performance = AnsightNetworkPerformance()
        performance.timeToFirstByteMilliseconds = -.infinity
        performance.downloadDurationMilliseconds = -1
        performance.uploadDurationMilliseconds = .nan
        performance.dnsMilliseconds = 0
        performance.downloadedBytes = -1
        performance.uploadedBytes = 0
        let normalized = performance.normalized()
        XCTAssertNil(normalized.timeToFirstByteMilliseconds)
        XCTAssertNil(normalized.downloadDurationMilliseconds)
        XCTAssertNil(normalized.uploadDurationMilliseconds)
        XCTAssertNil(normalized.downloadedBytes)
        XCTAssertEqual(normalized.dnsMilliseconds, 0)
        XCTAssertEqual(normalized.uploadedBytes, 0)
    }
}

private final class PerformanceTransactionStub: URLSessionTaskTransactionMetrics, @unchecked Sendable {
    var reused = false
    var finished = true
    var fetchType: URLSessionTaskMetrics.ResourceFetchType = .networkLoad
    private let origin = Date(timeIntervalSince1970: 1_000)
    override var resourceFetchType: URLSessionTaskMetrics.ResourceFetchType { fetchType }
    override var isReusedConnection: Bool { reused }
    override var domainLookupStartDate: Date? { reused ? nil : origin }
    override var domainLookupEndDate: Date? { reused ? nil : origin.addingTimeInterval(0.01) }
    override var connectStartDate: Date? { reused ? nil : origin.addingTimeInterval(0.01) }
    override var connectEndDate: Date? { reused ? nil : origin.addingTimeInterval(0.07) }
    override var secureConnectionStartDate: Date? { reused ? nil : origin.addingTimeInterval(0.03) }
    override var secureConnectionEndDate: Date? { reused ? nil : origin.addingTimeInterval(0.07) }
    override var requestStartDate: Date? { origin.addingTimeInterval(0.1) }
    override var requestEndDate: Date? { origin.addingTimeInterval(0.2) }
    override var responseStartDate: Date? { origin.addingTimeInterval(0.35) }
    override var responseEndDate: Date? { finished ? origin.addingTimeInterval(2.35) : nil }
    override var countOfRequestBodyBytesSent: Int64 { 10_000 }
    override var countOfResponseBodyBytesReceived: Int64 { 4_000_000 }
}
