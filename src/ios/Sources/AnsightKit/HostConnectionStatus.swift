import Foundation

public struct HostConnectionStatus: Sendable, Codable, Equatable {
    public let isRuntimeActive: Bool
    public let isConnected: Bool
    public let connectionState: HostConnectionState
    public let hasCachedSession: Bool
    public let hasSavedConfig: Bool
    public let hasBundledConfig: Bool
    public let summaryKind: HostConnectionSummaryKind
    public let summaryMessage: String
    public let hostId: String?
    public let hostName: String?
}
