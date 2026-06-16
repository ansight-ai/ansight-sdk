import Ansight
import Foundation

enum HarnessToolSchemas {
    static let stateSnapshotResult = AnsightToolSchema(json: .object([
        "type": .string("object"),
        "description": .string("Harness diagnostic state snapshot."),
    ]))

    static let reflectionRootsResult = AnsightToolSchema(json: .object([
        "type": .string("object"),
        "properties": .object([
            "roots": .object([
                "type": .string("array"),
                "description": .string("Registered harness reflection roots."),
            ]),
        ]),
    ]))

    static let inspectRootArguments = AnsightToolSchema(json: .object([
        "type": .string("object"),
        "properties": .object([
            "rootId": .object([
                "type": .string("string"),
                "description": .string("Root id returned by harness.reflection_roots.list."),
            ]),
        ]),
        "required": .array([.string("rootId")]),
    ]))

    static let inspectRootResult = AnsightToolSchema(json: .object([
        "type": .string("object"),
        "description": .string("Diagnostic state for one harness reflection root."),
    ]))
}
