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
                "items": .object([
                    "type": .string("object"),
                    "properties": .object([
                        "rootId": .object(["type": .string("string")]),
                        "name": .object(["type": .string("string")]),
                        "kind": .object(["type": .string("string")]),
                        "description": .object(["type": .string("string")]),
                        "hostRuntime": .object([
                            "type": .string("object"),
                            "description": .string("Runtime that owns and resolves the harness reflection root."),
                            "properties": .object([
                                "kind": .object(["type": .string("string")]),
                                "displayName": .object(["type": .string("string")]),
                                "platform": .object(["type": .string("string")]),
                                "engine": .object(["type": .string("string")]),
                            ]),
                            "required": .array([.string("kind"), .string("displayName")]),
                        ]),
                    ]),
                    "required": .array([
                        .string("rootId"),
                        .string("name"),
                        .string("kind"),
                        .string("description"),
                        .string("hostRuntime"),
                    ]),
                ]),
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
