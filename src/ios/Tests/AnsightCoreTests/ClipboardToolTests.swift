import XCTest
@testable import AnsightCore
@testable import AnsightToolsClipboard

final class ClipboardToolTests: XCTestCase {
    func testProtocolRoundTripPreservesTextAndClear() throws {
        let backend = FakeClipboard()
        let bridge = bridge(backend, policy: .readWrite)
        for text in ["", "  hello\n世界 🧗\t"] {
            XCTAssertEqual(try call(bridge, "clipboard.set_text", ["text": text]).type, "tool.result")
            let read = try call(bridge, "clipboard.get_text")
            XCTAssertEqual(read.payload.objectValue?["result"]?.objectValue?["text"], .string(text))
            XCTAssertEqual(read.payload.objectValue?["result"]?.objectValue?["hasText"], .bool(true))
        }
        XCTAssertEqual(try call(bridge, "clipboard.clear").type, "tool.result")
        XCTAssertEqual(try call(bridge, "clipboard.get_text").payload.objectValue?["result"]?.objectValue?["text"], .null)
    }

    func testValidationAccessErrorsAndPolicies() throws {
        let backend = FakeClipboard()
        let bridge = bridge(backend, policy: .readWrite)
        XCTAssertEqual(try call(bridge, "clipboard.set_text").type, "tool.error")
        let large = try call(bridge, "clipboard.set_text", ["text": String(repeating: "界", count: 21846)])
        XCTAssertEqual(large.payload.objectValue?["code"], .string("clipboard_text_too_large"))
        backend.unavailable = true
        XCTAssertEqual(try call(bridge, "clipboard.get_text").payload.objectValue?["code"], .string("clipboard_not_focused"))
        XCTAssertEqual(try call(self.bridge(FakeClipboard(), policy: .readOnly), "clipboard.set_text", ["text": "denied"]).type, "tool.error")
    }

    private func bridge(_ backend: FakeClipboard, policy: AnsightToolGuard) -> AnsightToolProtocolBridge {
        var registry: [String: RegisteredTool] = [:]
        for tool in AnsightClipboardTools.makeTools(backend: backend) {
            registry[tool.descriptor.id] = RegisteredTool(descriptor: tool.descriptor, execute: nil, executeJSON: { try (tool as! any AnsightJSONTool).execute(arguments: $0) })
        }
        return AnsightToolProtocolBridge(registry: registry, guardPolicy: policy)
    }
    private func call(_ bridge: AnsightToolProtocolBridge, _ tool: String, _ args: [String: String] = [:]) throws -> AnsightToolProtocolEnvelope {
        let envelope = AnsightToolProtocolEnvelope(type: "tool.call", id: UUID().uuidString, sessionId: "clipboard-test", payload: .object(["toolId": .string(tool), "arguments": .object(from: args)]))
        let request = String(data: try JSONEncoder().encode(envelope), encoding: .utf8)!
        let response = try XCTUnwrap(bridge.handleIfSupported(request))
        return try JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: Data(response.utf8))
    }
}

private final class FakeClipboard: ClipboardBackend, @unchecked Sendable {
    // Each test owns its backend and invokes its synchronous bridge serially.
    var text: String?
    var unavailable = false
    func getText() throws -> String? {
        if unavailable { throw ClipboardError(code: "clipboard_not_focused", message: "Focus the app.") }
        return text
    }
    func hasText() throws -> Bool { try getText() != nil }
    func setText(_ text: String) { self.text = text }
    func clear() { text = nil }
}

private extension JSONValue {
    var objectValue: [String: JSONValue]? {
        guard case .object(let values) = self else { return nil }
        return values
    }
}
