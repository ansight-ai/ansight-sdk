import AnsightCore
import Foundation

public final class SubmitKeyboardTool: AnsightTool {
    public init() {}

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(id: "ui.submit_keyboard", name: "Submit Keyboard",
            description: "Invokes the focused editor action without injecting a newline.",
            category: "ui", policy: .write, keywords: "keyboard ime submit return done search go send")
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        AnsightVisualTreeSupport.submitKeyboard()
    }
}
