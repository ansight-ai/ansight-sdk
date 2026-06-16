import Ansight
import Foundation

@objc(ANSHostConnectionResult)
public final class ANSHostConnectionResult: NSObject, @unchecked Sendable {
    @objc public let success: Bool
    @objc public let message: String
    @objc public let kind: String
    @objc public let source: String
    @objc public let reasonCode: String?
    @objc public let sessionId: String?
    @objc public let configId: String?
    @objc public let appId: String?
    @objc public let resolvedHostAddress: String?
    @objc public let hostId: String?
    @objc public let hostName: String?

    init(_ result: HostConnectionResult) {
        success = result.success
        message = result.message
        kind = result.kind.rawValue
        source = result.source.rawValue
        reasonCode = result.reasonCode ?? result.openSession?.reasonCode
        sessionId = result.openSession?.sessionId
        configId = result.openSession?.configId
        appId = result.openSession?.appId
        resolvedHostAddress = result.openSession?.resolvedHostAddress
        hostId = result.openSession?.hostId
        hostName = result.openSession?.hostName
    }
}
