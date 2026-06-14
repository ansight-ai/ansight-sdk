import Foundation

public struct AnsightToolExecutionResult: Sendable, Codable, Equatable {
    public let success: Bool
    public let message: String?
    public let errorCode: String?
    public let result: JSONValue?

    public init(success: Bool, message: String? = nil, errorCode: String? = nil, result: JSONValue? = nil) {
        self.success = success
        self.message = message
        self.errorCode = errorCode
        self.result = result
    }

    public static func success(_ result: JSONValue? = nil, message: String? = nil) -> AnsightToolExecutionResult {
        AnsightToolExecutionResult(success: true, message: message, result: result)
    }

    public static func failure(
        _ message: String,
        errorCode: String? = nil,
        result: JSONValue? = nil
    ) -> AnsightToolExecutionResult {
        AnsightToolExecutionResult(success: false, message: message, errorCode: errorCode, result: result)
    }
}
