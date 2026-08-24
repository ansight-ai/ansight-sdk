import Ansight
import Foundation

final class HarnessListReflectionRootsTool: AnsightTool, @unchecked Sendable {
    private let store: HarnessInspectionStore

    init(store: HarnessInspectionStore) {
        self.store = store
    }

    var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: "harness.reflection_roots.list",
            name: "List Harness Reflection Roots",
            description: "Lists the harness-registered state roots available for custom inspection.",
            category: "Harness",
            policy: .read,
            keywords: "harness reflection roots inspect custom state",
            resultSchema: HarnessToolSchemas.reflectionRootsResult
        )
    }

    func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        .success(
            .object(["roots": store.rootsJSON()]),
            message: "Harness reflection roots listed."
        )
    }
}
