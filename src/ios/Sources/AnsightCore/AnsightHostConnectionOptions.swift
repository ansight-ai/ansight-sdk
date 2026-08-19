import Foundation

public struct AnsightHostConnectionOptions: Sendable, Codable, Equatable {
    private enum CodingKeys: String, CodingKey {
        case savedConfigKey
        case connectionProfileRetentionSeconds
        case discoveryPort
        case allowCellularConnections
        case allowUnattendedProvisioning
        case bundledConfigJson
    }

    public var savedConfigKey: String
    public var connectionProfileRetentionSeconds: Int
    public var discoveryPort: Int?
    public var allowCellularConnections: Bool
    public var allowUnattendedProvisioning: Bool
    public var bundledConfigJson: String?

    public init(
        savedConfigKey: String = "ai.ansight.ios.saved-pairing",
        connectionProfileRetentionSeconds: Int = 14 * 24 * 60 * 60,
        discoveryPort: Int? = nil,
        allowCellularConnections: Bool = false,
        allowUnattendedProvisioning: Bool = false,
        bundledConfigJson: String? = nil
    ) {
        self.savedConfigKey = savedConfigKey
        self.connectionProfileRetentionSeconds = connectionProfileRetentionSeconds
        self.discoveryPort = discoveryPort
        self.allowCellularConnections = allowCellularConnections
        self.allowUnattendedProvisioning = allowUnattendedProvisioning
        self.bundledConfigJson = bundledConfigJson
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            savedConfigKey: try container.decodeIfPresent(String.self, forKey: .savedConfigKey)
                ?? "ai.ansight.ios.saved-pairing",
            connectionProfileRetentionSeconds: try container.decodeIfPresent(
                Int.self,
                forKey: .connectionProfileRetentionSeconds
            ) ?? 14 * 24 * 60 * 60,
            discoveryPort: try container.decodeIfPresent(Int.self, forKey: .discoveryPort),
            allowCellularConnections: try container.decodeIfPresent(
                Bool.self,
                forKey: .allowCellularConnections
            ) ?? false,
            allowUnattendedProvisioning: try container.decodeIfPresent(
                Bool.self,
                forKey: .allowUnattendedProvisioning
            ) ?? false,
            bundledConfigJson: try container.decodeIfPresent(String.self, forKey: .bundledConfigJson)
        )
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
