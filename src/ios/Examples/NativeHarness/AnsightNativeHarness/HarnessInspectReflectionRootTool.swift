import Ansight
import Foundation

final class HarnessInspectReflectionRootTool: AnsightTool, @unchecked Sendable {
    private let store: HarnessInspectionStore

    init(store: HarnessInspectionStore) {
        self.store = store
    }

    var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: "harness.reflection_root.inspect",
            name: "Inspect Harness Reflection Root",
            description: "Inspects one registered harness reflection root by rootId.",
            category: "Harness",
            scope: AnsightToolScope.read.rawValue,
            keywords: "harness reflection root inspect custom state",
            security: AnsightToolSecurity(
                level: .low,
                summary: "Reads state from a harness diagnostic root.",
                implications: [
                    AnsightToolSecurityImplications.inspectsRuntimeState,
                    AnsightToolSecurityImplications.metadataDisclosure,
                ]
            ),
            argumentsSchema: HarnessToolSchemas.inspectRootArguments,
            resultSchema: HarnessToolSchemas.inspectRootResult
        )
    }

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        let rootId = arguments["rootId"]?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !rootId.isEmpty else {
            return .failure("The argument 'rootId' is required.", errorCode: "MissingRootId")
        }

        guard let result = store.inspectRootJSON(rootId: rootId) else {
            return .failure("Unknown harness reflection root '\(rootId)'.", errorCode: "UnknownRoot")
        }

        return .success(result, message: "Harness reflection root inspected.")
    }
}
