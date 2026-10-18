import Foundation

/// A structured JSON tool backed by closures, with the standard guard and schema validation.
/// Handlers are synchronous on the transport context; dispatch UI or engine access explicitly.
public struct AnsightFunctionJSONTool: AnsightJSONTool {
    public let descriptor: AnsightToolDescriptor
    private let handler: @Sendable ([String: JSONValue]) throws -> AnsightToolExecutionResult
    private let availabilityHandler: @Sendable (AnsightToolAvailabilityContext) -> AnsightToolAvailability

    public init(descriptor: AnsightToolDescriptor,
        availability: @escaping @Sendable (AnsightToolAvailabilityContext) -> AnsightToolAvailability = { _ in .availableNow },
        handler: @escaping @Sendable ([String: JSONValue]) throws -> AnsightToolExecutionResult) {
        self.descriptor = descriptor
        self.handler = handler
        self.availabilityHandler = availability
    }

    public func availability(context: AnsightToolAvailabilityContext) -> AnsightToolAvailability {
        availabilityHandler(context)
    }

    public func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult {
        try handler(arguments)
    }
}
