import Foundation

public struct RecordedScreenView: Sendable, Codable, Equatable {
    public let name: String
    public let details: [String: String]
    public let capturedAtUtc: String
}
