import Foundation

public struct AnsightHostAutoProbeOptions: Sendable, Codable, Equatable {
    public var enabled: Bool
    public var initialDelayMilliseconds: Int
    public var probeIntervalMilliseconds: Int
    public var reconnectDelayMilliseconds: Int
    public var clientName: String?

    public static let enabledDefault = AnsightHostAutoProbeOptions()
    public static let disabledDefault = AnsightHostAutoProbeOptions(
        enabled: false,
        initialDelayMilliseconds: 0,
        probeIntervalMilliseconds: 5_000,
        reconnectDelayMilliseconds: 10_000
    )

    public init(
        enabled: Bool = true,
        initialDelayMilliseconds: Int = 1_000,
        probeIntervalMilliseconds: Int = 5_000,
        reconnectDelayMilliseconds: Int = 10_000,
        clientName: String? = nil
    ) {
        self.enabled = enabled
        self.initialDelayMilliseconds = initialDelayMilliseconds
        self.probeIntervalMilliseconds = probeIntervalMilliseconds
        self.reconnectDelayMilliseconds = reconnectDelayMilliseconds
        self.clientName = clientName
    }

    public mutating func validate() {
        initialDelayMilliseconds = max(0, initialDelayMilliseconds)
        probeIntervalMilliseconds = max(1_000, probeIntervalMilliseconds)
        reconnectDelayMilliseconds = max(1_000, reconnectDelayMilliseconds)
        if let trimmed = clientName?.trimmingCharacters(in: .whitespacesAndNewlines), trimmed.isEmpty {
            clientName = nil
        }
    }
}
