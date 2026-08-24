import Foundation

/// A tool that receives protocol arguments as structured JSON.
public protocol AnsightJSONTool: AnsightTool {
    func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult
}

public extension AnsightJSONTool {
    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        try execute(arguments: arguments.mapValues(JSONValue.string))
    }
}
