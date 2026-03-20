import Foundation

public struct AnsightOptions: Sendable, Codable {
    public var sampleFrequencyMilliseconds: Int
    public var retentionPeriodSeconds: Int
    public var enableFramesPerSecond: Bool
    public var additionalChannels: [AnsightChannel]
    public var toolGuard: AnsightToolGuard

    public init(
        sampleFrequencyMilliseconds: Int = 500,
        retentionPeriodSeconds: Int = 600,
        enableFramesPerSecond: Bool = true,
        additionalChannels: [AnsightChannel] = [],
        toolGuard: AnsightToolGuard = .disabled
    ) {
        self.sampleFrequencyMilliseconds = sampleFrequencyMilliseconds
        self.retentionPeriodSeconds = retentionPeriodSeconds
        self.enableFramesPerSecond = enableFramesPerSecond
        self.additionalChannels = additionalChannels
        self.toolGuard = toolGuard
    }

    public func validated() throws -> AnsightOptions {
        var copy = self
        copy.sampleFrequencyMilliseconds = max(50, min(sampleFrequencyMilliseconds, 60_000))
        copy.retentionPeriodSeconds = max(1, min(retentionPeriodSeconds, 86_400))
        try toolGuard.validate()
        return copy
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
    public var allowDiscoveryHintHostFallback: Bool

    public init(
        clientName: String,
        manualHostAddress: String,
        expectedAppId: String? = nil,
        profileOverride: [String: String] = [:],
        allowDiscoveryHintHostFallback: Bool = true
    ) {
        self.clientName = clientName
        self.manualHostAddress = manualHostAddress
        self.expectedAppId = expectedAppId
        self.profileOverride = profileOverride
        self.allowDiscoveryHintHostFallback = allowDiscoveryHintHostFallback
    }
}

public struct OpenSessionResult: Sendable, Codable {
    public let success: Bool
    public let message: String
    public let sessionId: String?
    public let configId: String?
    public let appId: String?
    public let resolvedHostAddress: String?
    public let usedEmbeddedDeveloperPairing: Bool
    public let discoverySource: String?

    public init(
        success: Bool,
        message: String,
        sessionId: String?,
        configId: String? = nil,
        appId: String? = nil,
        resolvedHostAddress: String? = nil,
        usedEmbeddedDeveloperPairing: Bool = false,
        discoverySource: String? = nil
    ) {
        self.success = success
        self.message = message
        self.sessionId = sessionId
        self.configId = configId
        self.appId = appId
        self.resolvedHostAddress = resolvedHostAddress
        self.usedEmbeddedDeveloperPairing = usedEmbeddedDeveloperPairing
        self.discoverySource = discoverySource
    }
}

public struct AnsightToolDescriptor: Sendable, Codable, Equatable {
    public let id: String
    public let name: String
    public let description: String
    public let category: String
    public let scope: String
    public let keywords: String
    public let argumentsSchema: AnsightToolSchema
    public let resultSchema: AnsightToolSchema

    public init(
        id: String,
        name: String,
        description: String = "",
        category: String = "Diagnostics",
        scope: String = AnsightToolScope.read.rawValue,
        keywords: String = "",
        argumentsSchema: AnsightToolSchema = .emptyObject,
        resultSchema: AnsightToolSchema = .emptyObject
    ) {
        self.id = id
        self.name = name
        self.description = description
        self.category = category
        self.scope = scope
        self.keywords = keywords
        self.argumentsSchema = argumentsSchema
        self.resultSchema = resultSchema
    }

    public var scopeValue: AnsightToolScope? {
        AnsightToolScope(rawValue: scope)
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
    public let executableTools: Int
    public let toolDiscoveryEnabled: Bool
    public let toolExecutionEnabled: Bool
    public let embeddedDeveloperPairingAvailable: Bool
    public let detectedBundledTools: [String]
    public let lastMetric: RecordedMetric?
    public let lastEvent: RecordedEvent?
    public let lastPairingConfigId: String?
    public let resolvedHostAddress: String?
    public let sessionMessage: String?

    public init(
        initialized: Bool,
        active: Bool,
        sessionOpen: Bool,
        metricsRecorded: Int,
        eventsRecorded: Int,
        registeredTools: Int,
        executableTools: Int,
        toolDiscoveryEnabled: Bool,
        toolExecutionEnabled: Bool,
        embeddedDeveloperPairingAvailable: Bool,
        detectedBundledTools: [String],
        lastMetric: RecordedMetric?,
        lastEvent: RecordedEvent?,
        lastPairingConfigId: String?,
        resolvedHostAddress: String?,
        sessionMessage: String?
    ) {
        self.initialized = initialized
        self.active = active
        self.sessionOpen = sessionOpen
        self.metricsRecorded = metricsRecorded
        self.eventsRecorded = eventsRecorded
        self.registeredTools = registeredTools
        self.executableTools = executableTools
        self.toolDiscoveryEnabled = toolDiscoveryEnabled
        self.toolExecutionEnabled = toolExecutionEnabled
        self.embeddedDeveloperPairingAvailable = embeddedDeveloperPairingAvailable
        self.detectedBundledTools = detectedBundledTools
        self.lastMetric = lastMetric
        self.lastEvent = lastEvent
        self.lastPairingConfigId = lastPairingConfigId
        self.resolvedHostAddress = resolvedHostAddress
        self.sessionMessage = sessionMessage
    }
}

public enum AnsightChannels {
    public static let unspecified = 255
}
