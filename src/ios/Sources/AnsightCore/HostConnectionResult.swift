import Foundation

public struct HostConnectionResult: Sendable, Codable, Equatable {
    public let success: Bool
    public let message: String
    public let kind: HostConnectionRequestKind
    public let source: HostConnectionSource
    public let reasonCode: String?
    public let openSession: OpenSessionResult?

    public init(
        success: Bool,
        message: String,
        kind: HostConnectionRequestKind,
        source: HostConnectionSource,
        reasonCode: String? = nil,
        openSession: OpenSessionResult? = nil
    ) {
        self.success = success
        self.message = message
        self.kind = kind
        self.source = source
        self.reasonCode = reasonCode
        self.openSession = openSession
    }
}
