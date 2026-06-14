import Foundation

public protocol AnsightTool: Sendable {
    var descriptor: AnsightToolDescriptor { get }
    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult
}
