import Foundation

internal struct RegisteredTool {
    let descriptor: AnsightToolDescriptor
    let execute: (([String: String]) throws -> AnsightToolExecutionResult)?
}
