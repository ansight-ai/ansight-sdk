import XCTest
@testable import AnsightCore
@testable import AnsightToolsReflection

final class ReflectionToolTests: XCTestCase {
    override func tearDown() {
        AnsightReflectionRootRegistry.clear()
        super.tearDown()
    }

    func testReflectionToolsListInspectDescribeSetAndInvokeRegisteredRoot() throws {
        let root = TestReflectionRoot()
        let registration = try AnsightReflectionRootRegistry.register(
            id: "session",
            target: root,
            displayName: "Session",
            referenceType: .strong
        )
        defer {
            registration.deregister()
        }

        let list = try resultObject(ListReflectionRootsTool().execute(arguments: [:]))
        let roots = try jsonArray(list["roots"])
        try assertSwiftHostRuntime(rootObject(roots, id: "session"))
        try assertSwiftHostRuntime(rootObject(roots, id: "runtime.snapshot"))

        let inspected = try resultObject(InspectObjectTool().execute(arguments: [
            "root": "session",
            "maxDepth": "2",
        ]))
        let snapshot = try jsonObject(inspected["snapshot"])
        let members = try jsonArray(snapshot["members"])
        XCTAssertTrue(members.contains { member in
            guard case .object(let object) = member,
                  case .string("title")? = object["name"] else {
                return false
            }
            return true
        })

        let child = try resultObject(InspectObjectTool().execute(arguments: [
            "root": "session",
            "path": "child.name",
        ]))
        let childSnapshot = try jsonObject(child["snapshot"])
        XCTAssertEqual(childSnapshot["value"], .string("Leaf"))

        let described = try resultObject(DescribeTypeTool().execute(arguments: [
            "root": "session",
        ]))
        XCTAssertEqual(described["kind"], .string("object"))
        XCTAssertTrue(try jsonArray(described["members"]).contains { member in
            guard case .object(let object) = member,
                  case .string("title")? = object["name"] else {
                return false
            }
            return true
        })

        let setResult = try resultObject(SetMemberValueTool().execute(arguments: [
            "root": "session",
            "path": "title",
            "valueJson": #""Orders""#,
        ]))
        XCTAssertEqual(setResult["updated"], .bool(true))
        XCTAssertEqual(root.title, "Orders")

        let invokeResult = try resultObject(InvokeMethodTool().execute(arguments: [
            "root": "session",
            "method": "resetTitle",
        ]))
        XCTAssertEqual(invokeResult["invoked"], .bool(true))
        XCTAssertEqual(root.title, "Checkout")
    }

    func testReflectionOptionsRestrictRootsAndTypeLookup() throws {
        let root = TestReflectionRoot()
        let registration = try AnsightReflectionRootRegistry.register(
            id: "session",
            target: root,
            displayName: "Session",
            referenceType: .strong
        )
        defer {
            registration.deregister()
        }

        let options = AnsightReflectionToolsOptions.createBuilder()
            .includeBuiltInRoots(false)
            .allowRoot("session")
            .allowTypePrefix("Allowed.")
            .build()

        let list = try resultObject(ListReflectionRootsTool(options: options).execute(arguments: [:]))
        let roots = try jsonArray(list["roots"])
        XCTAssertEqual(roots.count, 1)
        try assertSwiftHostRuntime(rootObject(roots, id: "session"))

        let deniedRoot = try InspectObjectTool(options: options).execute(arguments: [
            "root": "runtime.snapshot",
        ])
        XCTAssertFalse(deniedRoot.success)

        let deniedType = try DescribeTypeTool(options: options).execute(arguments: [
            "typeName": "Foundation.NSObject",
        ])
        XCTAssertFalse(deniedType.success)
    }

    private func resultObject(_ result: AnsightToolExecutionResult) throws -> [String: JSONValue] {
        XCTAssertTrue(result.success, result.message ?? "")
        guard case .object(let object)? = result.result else {
            throw ReflectionTestError.expectedObject
        }
        return object
    }

    private func jsonObject(_ value: JSONValue?) throws -> [String: JSONValue] {
        guard case .object(let object)? = value else {
            throw ReflectionTestError.expectedObject
        }
        return object
    }

    private func jsonArray(_ value: JSONValue?) throws -> [JSONValue] {
        guard case .array(let array)? = value else {
            throw ReflectionTestError.expectedArray
        }
        return array
    }

    private func rootObject(_ roots: [JSONValue], id: String) throws -> [String: JSONValue] {
        for root in roots {
            guard case .object(let object) = root else {
                continue
            }
            if case .string(let rootId)? = object["id"], rootId == id {
                return object
            }
        }
        throw ReflectionTestError.missingRoot
    }

    private func assertSwiftHostRuntime(_ root: [String: JSONValue]) throws {
        let hostRuntime = try jsonObject(root["hostRuntime"])
        XCTAssertEqual(hostRuntime["kind"], .string("swift"))
        XCTAssertEqual(hostRuntime["displayName"], .string("Swift/Objective-C runtime"))
        XCTAssertEqual(hostRuntime["platform"], .string("ios"))
        XCTAssertEqual(hostRuntime["engine"], .string("Swift"))
    }
}

private final class TestReflectionRoot: AnsightReflectionMutableRoot, AnsightReflectionInvokableRoot {
    var title = "Checkout"
    var child = TestReflectionChild()

    func setReflectionValue(path: String, value: JSONValue) throws -> JSONValue? {
        guard path == "title", case .string(let title) = value else {
            throw ReflectionTestError.unsupported
        }
        self.title = title
        return .string(title)
    }

    func invokeReflectionMethod(targetPath: String?, method: String, arguments: [JSONValue]) throws -> JSONValue? {
        guard method == "resetTitle" else {
            throw ReflectionTestError.unsupported
        }
        title = "Checkout"
        return .string(title)
    }
}

private final class TestReflectionChild {
    var name = "Leaf"
}

private enum ReflectionTestError: LocalizedError {
    case expectedObject
    case expectedArray
    case missingRoot
    case unsupported

    var errorDescription: String? {
        switch self {
        case .expectedObject:
            return "Expected JSON object."
        case .expectedArray:
            return "Expected JSON array."
        case .missingRoot:
            return "Expected reflection root."
        case .unsupported:
            return "Unsupported test reflection operation."
        }
    }
}
