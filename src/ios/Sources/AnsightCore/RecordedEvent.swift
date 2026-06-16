import Foundation

public struct RecordedEvent: Sendable, Codable, Equatable {
    public let id: String
    public let label: String
    public let type: AnsightEventType
    public let details: String?
    public let capturedAtUtc: String
    public let capturedAtEpochMs: Int64
    public let externalId: String?
    public let channel: Int
    public let sequence: Int64

    public init(
        id: String = UUID().uuidString,
        label: String,
        type: AnsightEventType,
        details: String? = nil,
        channel: Int,
        capturedAtUtc: String = AnsightClock.isoNow(),
        externalId: String? = nil,
        sequence: Int64 = 0
    ) {
        self.id = id
        self.label = label
        self.type = type
        self.details = details
        self.channel = channel
        self.capturedAtUtc = capturedAtUtc
        self.capturedAtEpochMs = AnsightClock.epochMilliseconds(fromISO8601: capturedAtUtc)
        self.externalId = externalId
        self.sequence = sequence
    }
}
