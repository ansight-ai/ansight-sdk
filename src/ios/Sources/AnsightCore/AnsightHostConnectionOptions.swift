import Foundation

public struct AnsightHostConnectionOptions: Sendable, Codable, Equatable {
    public var savedConfigKey: String
    public var connectionProfileRetentionSeconds: Int
    public var discoveryPort: Int?
    public var bundledConfigJson: String?

    public init(
        savedConfigKey: String = "ai.ansight.ios.saved-pairing",
        connectionProfileRetentionSeconds: Int = 14 * 24 * 60 * 60,
        discoveryPort: Int? = nil,
        bundledConfigJson: String? = nil
    ) {
        self.savedConfigKey = savedConfigKey
        self.connectionProfileRetentionSeconds = connectionProfileRetentionSeconds
        self.discoveryPort = discoveryPort
        self.bundledConfigJson = bundledConfigJson
    }

    public mutating func validate() throws {
        savedConfigKey = savedConfigKey.trimmingCharacters(in: .whitespacesAndNewlines)
        if savedConfigKey.isEmpty {
            savedConfigKey = "ai.ansight.ios.saved-pairing"
        }

        connectionProfileRetentionSeconds = max(60, connectionProfileRetentionSeconds)
        if let discoveryPort, !(1...65_535).contains(discoveryPort) {
            throw RuntimeError.invalidInput("HostConnection.discoveryPort must be between 1 and 65535.")
        }

        if let trimmed = bundledConfigJson?.trimmingCharacters(in: .whitespacesAndNewlines), trimmed.isEmpty {
            bundledConfigJson = nil
        }
    }
}
