import Foundation
import Network

public struct OperationResult: Sendable, Codable, Equatable {
    public let success: Bool
    public let message: String

    public static func success(_ message: String) -> OperationResult {
        OperationResult(success: true, message: message)
    }

    public static func failure(_ message: String) -> OperationResult {
        OperationResult(success: false, message: message)
    }
}
