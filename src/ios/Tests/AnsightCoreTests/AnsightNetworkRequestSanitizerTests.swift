import XCTest
@testable import AnsightCore

final class AnsightNetworkRequestSanitizerTests: XCTestCase {
    func testRedactsCredentialsInTypedNativeModel() throws {
        let request = AnsightNetworkRequest(
            id: "request-1",
            source: "capacitor.fetch",
            startedAtUtc: "2026-08-23T00:00:00Z",
            completedAtUtc: "2026-08-22T23:59:59Z",
            durationMilliseconds: 10,
            method: "get",
            url: "https://user:password@example.test/items?token=secret&visible=yes",
            requestHeaders: [
                AnsightNetworkHeader(name: "Authorization", value: "Bearer secret"),
            ],
            responseHeaders: [
                AnsightNetworkHeader(name: "Set-Cookie", value: "session=secret"),
            ]
        )

        let sanitized = AnsightNetworkRequestSanitizer.sanitize(request)

        XCTAssertEqual(sanitized.method, "GET")
        XCTAssertEqual(sanitized.completedAtUtc, sanitized.startedAtUtc)
        XCTAssertFalse(sanitized.url.contains("password"))
        XCTAssertFalse(sanitized.url.contains("token=secret"))
        XCTAssertEqual(sanitized.requestHeaders.first?.value, "<redacted>")
        XCTAssertEqual(sanitized.responseHeaders.first?.value, "<redacted>")
    }

    func testRedactsCloudSignaturesAndCapturedTextBodies() {
        let request = AnsightNetworkRequest(
            id: "request-cloud",
            source: "flutter.http",
            startedAtUtc: "2026-08-23T00:00:00Z",
            completedAtUtc: "2026-08-23T00:00:01Z",
            durationMilliseconds: 10,
            method: "post",
            url: "https://blob.test/a?sv=1&sp=rw&se=tomorrow&sig=azure-secret&safe=yes",
            requestBody: AnsightNetworkBody(
                contentType: "application/json",
                encoding: "utf8",
                data: #"{"token":"body-secret","visible":"yes"}"#,
                capturedBytes: 39,
                totalBytes: 39,
                truncated: false
            )
        )

        let sanitized = AnsightNetworkRequestSanitizer.sanitize(request)
        XCTAssertFalse(sanitized.url.contains("azure-secret"))
        XCTAssertFalse(sanitized.url.contains("tomorrow"))
        XCTAssertTrue(sanitized.url.contains("safe=yes"))
        XCTAssertFalse(sanitized.requestBody?.data.contains("body-secret") ?? true)
        XCTAssertTrue(sanitized.requestBody?.data.contains("visible") ?? false)
    }
}
