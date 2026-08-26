import Foundation

/// Opt-in capture policy for HTTP(S) requests observed by the Apple URL Loading System.
public struct AnsightNetworkCaptureOptions: Sendable, Codable, Equatable {
    public static let defaultMaximumBodyBytes = 64 * 1_024
    public static var defaultEnabled: Bool {
        #if targetEnvironment(simulator)
        true
        #else
        false
        #endif
    }

    public var enabled: Bool
    public var redactSensitiveData: Bool
    public var includeRequestHeaders: Bool
    public var includeResponseHeaders: Bool
    public var includeQueryString: Bool
    public var includeBodySizes: Bool
    public var captureRequestBody: Bool
    public var captureResponseBody: Bool
    public var maximumBodyBytes: Int
    public var captureBinaryBodies: Bool

    public init(
        enabled: Bool = defaultEnabled,
        redactSensitiveData: Bool = true,
        includeRequestHeaders: Bool = true,
        includeResponseHeaders: Bool = true,
        includeQueryString: Bool = true,
        includeBodySizes: Bool = true,
        captureRequestBody: Bool = true,
        captureResponseBody: Bool = true,
        maximumBodyBytes: Int = defaultMaximumBodyBytes,
        captureBinaryBodies: Bool = false
    ) {
        self.enabled = enabled
        self.redactSensitiveData = redactSensitiveData
        self.includeRequestHeaders = includeRequestHeaders
        self.includeResponseHeaders = includeResponseHeaders
        self.includeQueryString = includeQueryString
        self.includeBodySizes = includeBodySizes
        self.captureRequestBody = captureRequestBody
        self.captureResponseBody = captureResponseBody
        self.maximumBodyBytes = maximumBodyBytes
        self.captureBinaryBodies = captureBinaryBodies
    }

    mutating func validate() {
        maximumBodyBytes = max(0, maximumBodyBytes)
    }
}
