import Foundation

struct LiveSessionOpenAttempt: Sendable {
    let result: OperationResult
    let reasonCode: String?
    let authenticatedSessionId: String?
    let grant: PairingGrantV2?

    init(
        result: OperationResult,
        reasonCode: String?,
        authenticatedSessionId: String? = nil,
        grant: PairingGrantV2? = nil
    ) {
        self.result = result
        self.reasonCode = reasonCode
        self.authenticatedSessionId = authenticatedSessionId
        self.grant = grant
    }
}
