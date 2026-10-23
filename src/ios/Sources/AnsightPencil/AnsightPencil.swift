import AnsightCore
import AnsightPencilRuntime
import Foundation

public enum AnsightPencil {
    public static let plugin = AnsightPlugin(id: "ansight.pencil") { runtime in
        try register(runtime: runtime)
    }

    public static var isSupported: Bool { ANSPencilRuntime.isSupported }

    /// Registers the optional tool after SDK initialization. Returns false off Simulator.
    /// Registration grants only Pencil tools; global disable still wins. It does not install UIKit hooks.
    @discardableResult
    public static func register(runtime: AnsightRuntime = .shared) throws -> Bool {
        guard isSupported else { return false }
        guard runtime.snapshot().initialized else { throw RegistrationError.notInitialized }
        for tool in tools(runtime: runtime) where !runtime.isToolRegistered(tool.descriptor.id) {
            try runtime.registerTool(tool)
        }
        runtime.grantToolAccess("ui.simulate_pen", through: .write)
        runtime.grantToolAccess("ui.simulate_pen_framework", through: .write)
        return true
    }

    public enum RegistrationError: Error, LocalizedError {
        case notInitialized
        public var errorDescription: String? {
            "Initialize the Ansight SDK before registering Pencil simulation."
        }
    }

    /// Explicit opt-in: add these tools to the runtime. No hooks are installed until permitted playback.
    public static func tools(runtime: AnsightRuntime = .shared) -> [any AnsightTool] {
        isSupported ? [SimulatorPenTool(runtime: runtime)] : []
    }
}

public extension AnsightOptionsBuilder {
    /// Opts into and authorizes only Simulator Pencil tools during initialization.
    @discardableResult
    func withSimulatorPenInput() -> AnsightOptionsBuilder {
        withPlugin(AnsightPencil.plugin)
    }
}

public extension AnsightRuntime {
    /// Enables experimental Simulator playback with exact Pencil tool grants.
    func registerPencilSimulation() throws {
        try AnsightPencil.register(runtime: self)
    }
}

private struct SimulatorPenTool: AnsightJSONTool {
    let runtime: AnsightRuntime
    private var sampleSchema: JSONValue {
        var properties: [String: JSONValue] = [:]
        for key in ["x", "y", "pressure", "altitudeRadians", "azimuthRadians", "rollRadians", "distance", "eventTimestamp"] {
            properties[key] = .object(["type": .string("number")])
        }
        properties["phase"] = .object(["type": .string("string")])
        return .object(["type": .string("object"), "additionalProperties": .bool(false),
            "properties": .object(properties), "required": .array([.string("phase"), .string("x"), .string("y")])])
    }
    var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(id: "ui.simulate_pen", name: "Simulator pen properties",
            description: "Simulator only. Prepare an explicit drawing target and optional named idle hover recognizer, arm transport-stamped HID contacts, deliver scoped hover, or clear this run. Expires after 35 seconds.",
            category: "ui", policy: .write, keywords: "pencil stylus simulator simulation",
            argumentsSchema: AnsightToolSchema(json: .object([
                "type": .string("object"), "additionalProperties": .bool(false),
                "required": .array([.string("action"), .string("runId")]),
                "properties": .object([
                    "action": .object(["type": .string("string"), "enum": .array([.string("prepare"), .string("arm"), .string("hover"), .string("finish")])]),
                    "runId": .object(["type": .string("string")]),
                    "target": .object(["type": .string("string")]),
                    "hoverRecognizer": .object(["type": .string("string")]),
                    "samples": .object(["type": .string("array"), "maxItems": .integer(2000), "items": sampleSchema]),
                    "sample": sampleSchema
                ])
            ])))
    }
    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        .failure("Use structured JSON arguments.", errorCode: "pen_invalid_arguments")
    }
    func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult {
        guard runtime.snapshot().initialized else {
            return .failure("Initialize the Ansight runtime before playback.", errorCode: "pen_unavailable")
        }
        let json = String(decoding: try JSONEncoder().encode(arguments), as: UTF8.self)
        return try JSONDecoder().decode(AnsightToolExecutionResult.self,
            from: Data(ANSPencilRuntime.execute(argumentsJson: json).utf8))
    }
}
