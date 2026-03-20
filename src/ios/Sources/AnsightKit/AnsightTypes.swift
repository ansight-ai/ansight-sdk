import Foundation

public struct AnsightOptions: Sendable {
    public var sampleFrequencyMilliseconds: Int
    public var retentionPeriodSeconds: Int
    public var enableFramesPerSecond: Bool
    public var additionalChannels: [AnsightChannel]

    public init(
        sampleFrequencyMilliseconds: Int = 500,
        retentionPeriodSeconds: Int = 600,
        enableFramesPerSecond: Bool = true,
        additionalChannels: [AnsightChannel] = []
    ) {
        self.sampleFrequencyMilliseconds = sampleFrequencyMilliseconds
        self.retentionPeriodSeconds = retentionPeriodSeconds
        self.enableFramesPerSecond = enableFramesPerSecond
        self.additionalChannels = additionalChannels
    }
}

public struct AnsightChannel: Sendable, Codable, Hashable {
    public let id: Int
    public let name: String
    public let colorHex: String?

    public init(id: Int, name: String, colorHex: String? = nil) {
        self.id = id
        self.name = name
        self.colorHex = colorHex
    }
}

public enum AnsightEventType: String, Sendable, Codable, CaseIterable {
    case event = "Event"
    case debug = "Debug"
    case info = "Info"
    case warning = "Warning"
    case error = "Error"
    case exception = "Exception"
    case gc = "Gc"
    case navigation = "Navigation"
}

public struct PairingOpenOptions: Sendable {
    public var clientName: String
    public var manualHostAddress: String
    public var expectedAppId: String?
    public var profileOverride: [String: String]

    public init(
        clientName: String,
        manualHostAddress: String,
        expectedAppId: String? = nil,
        profileOverride: [String: String] = [:]
    ) {
        self.clientName = clientName
        self.manualHostAddress = manualHostAddress
        self.expectedAppId = expectedAppId
        self.profileOverride = profileOverride
    }
}

public struct OpenSessionResult: Sendable, Codable {
    public let success: Bool
    public let message: String
    public let sessionId: String?

    public init(success: Bool, message: String, sessionId: String?) {
        self.success = success
        self.message = message
        self.sessionId = sessionId
    }
}

public struct AnsightToolDescriptor: Sendable, Codable, Hashable {
    public let id: String
    public let name: String
    public let scope: String

    public init(id: String, name: String, scope: String = "Read") {
        self.id = id
        self.name = name
        self.scope = scope
    }
}

public struct RecordedMetric: Sendable, Codable {
    public let value: Int64
    public let channel: Int
    public let capturedAtEpochMs: Int64
}

public struct RecordedEvent: Sendable, Codable {
    public let id: String
    public let label: String
    public let type: AnsightEventType
    public let details: String?
    public let channel: Int
    public let capturedAtEpochMs: Int64
}

public struct AnsightDebugSnapshot: Sendable, Codable {
    public let initialized: Bool
    public let active: Bool
    public let sessionOpen: Bool
    public let metricsRecorded: Int
    public let eventsRecorded: Int
    public let registeredTools: Int
    public let lastMetric: RecordedMetric?
    public let lastEvent: RecordedEvent?
    public let sessionMessage: String?
}

public enum AnsightChannels {
    public static let unspecified = 255
}
