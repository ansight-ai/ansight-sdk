import Foundation

public protocol AnsightTool: Sendable {
    var descriptor: AnsightToolDescriptor { get }
    func availability(context: AnsightToolAvailabilityContext) -> AnsightToolAvailability
    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult
}

public extension AnsightTool {
    func availability(context: AnsightToolAvailabilityContext) -> AnsightToolAvailability {
        .availableNow
    }
}
