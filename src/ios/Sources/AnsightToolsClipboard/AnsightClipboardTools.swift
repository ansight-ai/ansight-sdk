import AnsightCore
import Foundation

public enum AnsightClipboardToolIds {
    public static let getText = "clipboard.get_text"
    public static let hasText = "clipboard.has_text"
    public static let setText = "clipboard.set_text"
    public static let clear = "clipboard.clear"
}

internal protocol ClipboardBackend: Sendable {
    func getText() throws -> String?
    func hasText() throws -> Bool
    func setText(_ text: String) throws
    func clear() throws
}

internal struct ClipboardError: Error {
    let code: String
    let message: String
}

public enum AnsightClipboardTools {
    public static func tools() -> [any AnsightTool] { makeTools(backend: nil) }

    internal static func makeTools(backend: (any ClipboardBackend)?) -> [any AnsightTool] {
        [AnsightClipboardToolIds.getText, AnsightClipboardToolIds.hasText,
         AnsightClipboardToolIds.setText, AnsightClipboardToolIds.clear].map {
            ClipboardTool(id: $0, backend: backend)
        }
    }
}

public extension AnsightRuntime {
    func registerClipboardTools() throws {
        for tool in AnsightClipboardTools.tools() { try registerTool(tool) }
    }
}

internal final class ClipboardTool: AnsightJSONTool {
    private let id: String
    private let backend: (any ClipboardBackend)?

    init(id: String, backend: (any ClipboardBackend)? = nil) {
        self.id = id
        self.backend = backend
    }

    var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: id, name: id, description: "Access the system clipboard of the connected app. Reads may require platform paste permission. Text is limited to 65536 UTF-8 bytes.",
            category: "clipboard", policy: id == AnsightClipboardToolIds.setText || id == AnsightClipboardToolIds.clear ? .write : .read,
            keywords: "clipboard copy paste text",
            argumentsSchema: ClipboardSchemas.arguments(id), resultSchema: ClipboardSchemas.result(id)
        )
    }

    func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult {
        var strings: [String: String] = [:]
        for (key, value) in arguments {
            guard case .string(let text) = value else {
                return .failure("Clipboard arguments must be strings.", errorCode: "clipboard_invalid_arguments")
            }
            strings[key] = text
        }
        return try execute(arguments: strings)
    }

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            guard arguments.keys.allSatisfy({ $0 == AnsightToolExecutionArgumentNames.requestId || $0 == AnsightToolExecutionArgumentNames.sessionId || (id == AnsightClipboardToolIds.setText && $0 == "text") }) else {
                throw ClipboardError(code: "clipboard_invalid_arguments", message: "Unexpected clipboard argument.")
            }
            if id == AnsightClipboardToolIds.setText {
                guard let text = arguments["text"] else {
                    throw ClipboardError(code: "clipboard_invalid_arguments", message: "The text argument is required (an empty string is allowed).")
                }
                try validateText(text)
            }
            let value: JSONValue
            if let backend {
                value = try perform(backend, arguments)
            } else if Thread.isMainThread {
                value = try MainActor.assumeIsolated { try perform(NativeClipboardBackend(), arguments) }
            } else {
                value = try DispatchQueue.main.sync {
                    try MainActor.assumeIsolated { try perform(NativeClipboardBackend(), arguments) }
                }
            }
            return .success(value)
        } catch let error as ClipboardError {
            return .failure(error.message, errorCode: error.code)
        } catch {
            return .failure("The clipboard operation failed.", errorCode: "clipboard_operation_failed")
        }
    }

    private func perform(_ target: any ClipboardBackend, _ args: [String: String]) throws -> JSONValue {
        switch id {
        case AnsightClipboardToolIds.getText:
            let text = try target.getText()
            if let text { try validateText(text) }
            return .object(["text": text.map(JSONValue.string) ?? .null, "hasText": .bool(text != nil)])
        case AnsightClipboardToolIds.hasText:
            return .object(["hasText": .bool(try target.hasText())])
        case AnsightClipboardToolIds.setText:
            try target.setText(args["text"]!)
            return .object(["updated": .bool(true)])
        default:
            try target.clear()
            return .object(["cleared": .bool(true)])
        }
    }

    private func validateText(_ text: String) throws {
        if text.utf8.count > 65_536 {
            throw ClipboardError(code: "clipboard_text_too_large", message: "Clipboard text exceeds 65536 UTF-8 bytes.")
        }
    }
}

internal enum ClipboardSchemas {
    static func arguments(_ id: String) -> AnsightToolSchema {
        object(id == AnsightClipboardToolIds.setText ? ["text": primitive("string")] : [:])
    }
    static func result(_ id: String) -> AnsightToolSchema {
        switch id {
        case AnsightClipboardToolIds.getText:
            return object(["text": .object(["type": .array([.string("string"), .string("null")])]), "hasText": primitive("boolean")])
        case AnsightClipboardToolIds.hasText: return object(["hasText": primitive("boolean")])
        case AnsightClipboardToolIds.setText: return object(["updated": primitive("boolean")])
        default: return object(["cleared": primitive("boolean")])
        }
    }
    private static func primitive(_ type: String) -> JSONValue { .object(["type": .string(type)]) }
    private static func object(_ properties: [String: JSONValue]) -> AnsightToolSchema {
        AnsightToolSchema(json: .object([
            "type": .string("object"), "additionalProperties": .bool(false),
            "properties": .object(properties), "required": .array(properties.keys.sorted().map(JSONValue.string))
        ]))
    }
}
