import Ansight
import Foundation

@objc(ANSOperationResult)
public final class ANSOperationResult: NSObject, @unchecked Sendable {
    @objc public let success: Bool
    @objc public let message: String

    init(_ result: OperationResult) {
        success = result.success
        message = result.message
    }

    init(success: Bool, message: String) {
        self.success = success
        self.message = message
    }
}
