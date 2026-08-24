import Foundation

internal struct RegisteredTool {
    let descriptor: AnsightToolDescriptor
    let availability: (AnsightToolAvailabilityContext) -> AnsightToolAvailability
    let execute: (([String: String]) throws -> AnsightToolExecutionResult)?
    let executeJSON: (([String: JSONValue]) throws -> AnsightToolExecutionResult)?

    init(
        descriptor: AnsightToolDescriptor,
        availability: @escaping (AnsightToolAvailabilityContext) -> AnsightToolAvailability = { _ in .availableNow },
        execute: (([String: String]) throws -> AnsightToolExecutionResult)?,
        executeJSON: (([String: JSONValue]) throws -> AnsightToolExecutionResult)? = nil
    ) {
        self.descriptor = descriptor
        self.availability = availability
        self.execute = execute
        self.executeJSON = executeJSON
    }
}
