import AnsightCore
import Foundation

internal enum AnsightReflectionSupport {
    static func listRoots(runtime: AnsightRuntime, options: AnsightReflectionToolsOptions) -> JSONValue {
        let descriptors = roots(runtime: runtime, options: options).map(rootDescriptor)
        return .object([
            "roots": .array(descriptors),
            "count": .integer(Int64(descriptors.count)),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func inspectObject(
        runtime: AnsightRuntime,
        options: AnsightReflectionToolsOptions,
        arguments: [String: String]
    ) throws -> JSONValue {
        let target = try resolveTarget(runtime: runtime, options: options, arguments: arguments)
        let maxDepth = integer(arguments, key: "maxDepth", defaultValue: 1, minimum: 0, maximum: 8)
        let maxItems = integer(arguments, key: "maxItemsPerCollection", defaultValue: 64, minimum: 1, maximum: 512)
        return .object([
            "root": .string(target.root.id),
            "path": target.path.map(JSONValue.string) ?? .null,
            "snapshot": snapshot(target.value, root: target.root.id, path: target.path, maxDepth: maxDepth, maxItems: maxItems),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func describeType(
        runtime: AnsightRuntime,
        options: AnsightReflectionToolsOptions,
        arguments: [String: String]
    ) throws -> JSONValue {
        if let typeName = (arguments["typeName"] ?? arguments["type"])?.trimmedNonEmpty {
            guard options.isTypeAllowed(typeName) else {
                throw AnsightReflectionToolError.notAllowed("Reflection type '\(typeName)' is not allow-listed.")
            }
            return describeTypeName(typeName)
        }

        let target = try resolveTarget(runtime: runtime, options: options, arguments: arguments)
        return describeValue(target.value)
    }

    static func setMemberValue(
        runtime: AnsightRuntime,
        options: AnsightReflectionToolsOptions,
        arguments: [String: String]
    ) throws -> JSONValue {
        let rootId = try required(arguments, keys: ["root", "rootId"], name: "root")
        let path = try required(arguments, keys: ["path", "member", "name"], name: "path")
        let root = try resolveRoot(runtime: runtime, options: options, id: rootId)
        guard let rootValue = root.value else {
            throw AnsightReflectionToolError.unavailable("Reflection root '\(root.id)' is unavailable.")
        }

        let value = try replacementValue(arguments)
        let updatedValue: JSONValue?
        if let mutable = rootValue as? AnsightReflectionMutableRoot {
            updatedValue = try mutable.setReflectionValue(path: path, value: value)
        } else {
            throw AnsightReflectionToolError.unsupported(
                "Reflection root '\(root.id)' does not opt in to writes. Conform the root object to AnsightReflectionMutableRoot to support reflect.set_member_value."
            )
        }

        let snapshotValue = try? resolveTarget(runtime: runtime, options: options, rootId: root.id, path: path).value
        return .object([
            "root": .string(root.id),
            "path": .string(path),
            "updated": .bool(true),
            "snapshot": updatedValue.map(jsonSnapshot) ?? snapshot(snapshotValue, root: root.id, path: path, maxDepth: 1, maxItems: 64),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    static func invokeMethod(
        runtime: AnsightRuntime,
        options: AnsightReflectionToolsOptions,
        arguments: [String: String]
    ) throws -> JSONValue {
        let rootId = try required(arguments, keys: ["root", "rootId"], name: "root")
        let method = try required(arguments, keys: ["method", "name"], name: "method")
        let targetPath = arguments["targetPath"]?.trimmedNonEmpty ?? arguments["path"]?.trimmedNonEmpty
        let root = try resolveRoot(runtime: runtime, options: options, id: rootId)
        guard let rootValue = root.value else {
            throw AnsightReflectionToolError.unavailable("Reflection root '\(root.id)' is unavailable.")
        }

        let values = try methodArguments(arguments)
        let returnedValue: JSONValue?
        if let invokable = rootValue as? AnsightReflectionInvokableRoot {
            returnedValue = try invokable.invokeReflectionMethod(
                targetPath: targetPath,
                method: method,
                arguments: values
            )
        } else if let targetPath,
                  let target = try? resolveTarget(runtime: runtime, options: options, rootId: root.id, path: targetPath).value,
                  let invokable = target as? AnsightReflectionInvokableRoot {
            returnedValue = try invokable.invokeReflectionMethod(
                targetPath: nil,
                method: method,
                arguments: values
            )
        } else {
            throw AnsightReflectionToolError.unsupported(
                "Reflection root '\(root.id)' does not opt in to method invocation. Conform the root object to AnsightReflectionInvokableRoot to support reflect.invoke_method."
            )
        }

        return .object([
            "root": .string(root.id),
            "targetPath": targetPath.map(JSONValue.string) ?? .null,
            "signature": .string("\(method)(\(values.map { _ in "JSONValue" }.joined(separator: ",")))"),
            "invoked": .bool(true),
            "returnSnapshot": returnedValue.map(jsonSnapshot) ?? .object(["kind": .string("null"), "runtimeType": .null, "preview": .null]),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    private static func roots(runtime: AnsightRuntime, options: AnsightReflectionToolsOptions) -> [ResolvedReflectionRoot] {
        let builtIn = options.includeBuiltInRoots ? [ResolvedReflectionRoot(
            id: "runtime.snapshot",
            metadata: AnsightReflectionRootMetadata(
                displayName: "Ansight Runtime Snapshot",
                description: "Current SDK runtime state."
            ),
            referenceType: .getter,
            value: runtime.snapshot(),
            resolutionError: nil
        )] : []

        let registered = AnsightReflectionRootRegistry.snapshot().map { root -> ResolvedReflectionRoot in
            ResolvedReflectionRoot(
                id: root.id,
                metadata: root.metadata,
                referenceType: root.referenceType,
                value: root.resolve(),
                resolutionError: nil
            )
        }

        return (builtIn + registered)
            .filter { options.isRootAllowed($0.id) }
            .sorted {
            $0.id.localizedCaseInsensitiveCompare($1.id) == .orderedAscending
        }
    }

    private static func rootDescriptor(_ root: ResolvedReflectionRoot) -> JSONValue {
        .object([
            "id": .string(root.id),
            "metadata": .object([
                "displayName": .string(root.metadata.displayName),
                "description": root.metadata.description.map(JSONValue.string) ?? .null,
                "hints": .array(root.metadata.hints.map(JSONValue.string)),
            ]),
            "hostRuntime": hostRuntimeDescriptor(),
            "referenceType": .string(root.referenceType.rawValue),
            "available": .bool(root.value != nil),
            "runtimeType": root.value.map { .string(typeName(of: $0)) } ?? .null,
            "type": root.value.map { .string(typeName(of: $0)) } ?? .null,
            "memberVisibility": .string("PublicOnly"),
            "resolutionError": root.resolutionError.map(JSONValue.string) ?? .null,
        ])
    }

    private static func hostRuntimeDescriptor() -> JSONValue {
        .object([
            "kind": .string("swift"),
            "displayName": .string("Swift/Objective-C runtime"),
            "platform": .string("ios"),
            "engine": .string("Swift"),
        ])
    }

    private static func resolveTarget(
        runtime: AnsightRuntime,
        options: AnsightReflectionToolsOptions,
        arguments: [String: String]
    ) throws -> ReflectionTarget {
        let rootId = try required(arguments, keys: ["root", "rootId"], name: "root")
        return try resolveTarget(runtime: runtime, options: options, rootId: rootId, path: arguments["path"]?.trimmedNonEmpty)
    }

    private static func resolveTarget(
        runtime: AnsightRuntime,
        options: AnsightReflectionToolsOptions,
        rootId: String,
        path: String?
    ) throws -> ReflectionTarget {
        let root = try resolveRoot(runtime: runtime, options: options, id: rootId)
        guard var value = root.value else {
            throw AnsightReflectionToolError.unavailable("Reflection root '\(root.id)' is unavailable.")
        }

        if let path {
            for segment in path.split(separator: ".").map(String.init).filter({ !$0.isEmpty }) {
                guard let next = readSegment(segment, from: value) else {
                    throw AnsightReflectionToolError.unavailable("Path segment '\(segment)' could not be resolved.")
                }
                value = next
            }
        }

        return ReflectionTarget(root: root, path: path, value: value)
    }

    private static func resolveRoot(
        runtime: AnsightRuntime,
        options: AnsightReflectionToolsOptions,
        id: String
    ) throws -> ResolvedReflectionRoot {
        let normalizedId = id.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalizedId.isEmpty else {
            throw AnsightReflectionToolError.invalidArgument("Reflection root is required.")
        }

        guard let root = roots(runtime: runtime, options: options).first(where: { $0.id == normalizedId }) else {
            throw AnsightReflectionToolError.invalidArgument("Unknown reflection root '\(normalizedId)'.")
        }

        return root
    }

    private static func snapshot(_ value: Any?, root: String, path: String?, maxDepth: Int, maxItems: Int) -> JSONValue {
        let object = valueSummary(value)
        guard case .object(var fields) = object else {
            return object
        }

        fields["root"] = .string(root)
        fields["path"] = path.map(JSONValue.string) ?? .null

        guard let unwrapped = unwrapOptional(value), maxDepth > 0, !isScalar(unwrapped) else {
            return .object(fields)
        }

        let mirror = Mirror(reflecting: unwrapped)
        if mirror.displayStyle == .collection || mirror.displayStyle == .set {
            fields["items"] = .array(Array(mirror.children.prefix(maxItems)).enumerated().map { index, child in
                .object([
                    "index": .integer(Int64(index)),
                    "value": snapshot(child.value, root: root, path: joinPath(path, "[\(index)]"), maxDepth: maxDepth - 1, maxItems: maxItems),
                ])
            })
            fields["truncated"] = .bool(mirror.children.count > maxItems)
            return .object(fields)
        }

        if mirror.displayStyle == .dictionary {
            fields["items"] = .array(Array(mirror.children.prefix(maxItems)).enumerated().map { index, child in
                let tuple = Mirror(reflecting: child.value)
                let parts = Array(tuple.children)
                return .object([
                    "index": .integer(Int64(index)),
                    "key": parts.first.map { valueSummary($0.value) } ?? .null,
                    "value": parts.dropFirst().first.map {
                        snapshot($0.value, root: root, path: joinPath(path, "[\(index)]"), maxDepth: maxDepth - 1, maxItems: maxItems)
                    } ?? .null,
                ])
            })
            fields["truncated"] = .bool(mirror.children.count > maxItems)
            return .object(fields)
        }

        fields["members"] = .array(namedChildren(of: unwrapped).prefix(maxItems).map { child in
            .object([
                "name": .string(child.name),
                "declaringType": .string(typeName(of: unwrapped)),
                "writable": .bool(false),
                "value": snapshot(child.value, root: root, path: joinPath(path, child.name), maxDepth: maxDepth - 1, maxItems: maxItems),
            ])
        })
        fields["methods"] = .array(methodDescriptors(for: unwrapped))
        return .object(fields)
    }

    private static func valueSummary(_ value: Any?) -> JSONValue {
        guard let value = unwrapOptional(value) else {
            return .object([
                "kind": .string("null"),
                "runtimeType": .null,
                "preview": .null,
            ])
        }

        var fields: [String: JSONValue] = [
            "kind": .string(kind(of: value)),
            "runtimeType": .string(typeName(of: value)),
            "preview": preview(value).map(JSONValue.string) ?? .null,
        ]

        if let scalar = scalarJSONValue(value) {
            fields["value"] = scalar
        }

        return .object(fields)
    }

    private static func jsonSnapshot(_ value: JSONValue) -> JSONValue {
        .object([
            "kind": .string("json"),
            "runtimeType": .string("JSONValue"),
            "preview": jsonPreview(value).map(JSONValue.string) ?? .null,
            "value": value,
        ])
    }

    private static func jsonPreview(_ value: JSONValue) -> String? {
        switch value {
        case .object, .array:
            return try? value.jsonString()
        case .string(let value):
            return value
        case .integer(let value):
            return String(value)
        case .number(let value):
            return String(value)
        case .bool(let value):
            return String(value)
        case .null:
            return nil
        }
    }

    private static func describeValue(_ value: Any?) -> JSONValue {
        guard let value = unwrapOptional(value) else {
            return describeTypeFields(
                typeName: "nil",
                assemblyName: "Swift",
                namespace: nil,
                kind: "null",
                baseType: nil,
                members: [],
                methods: []
            )
        }

        return describeTypeFields(
            typeName: typeName(of: value),
            assemblyName: moduleName(of: value),
            namespace: namespace(of: value),
            kind: kind(of: value),
            baseType: superclassName(of: value),
            members: namedChildren(of: value).map { child in
                .object([
                    "name": .string(child.name),
                    "memberType": .string("field"),
                    "declaringType": .string(typeName(of: value)),
                    "type": .string(typeName(of: child.value)),
                    "readable": .bool(true),
                    "writable": .bool(false),
                    "visibility": .string("public"),
                ])
            },
            methods: methodDescriptors(for: value)
        )
    }

    private static func describeTypeName(_ typeName: String) -> JSONValue {
        let resolvedClass: AnyClass? = NSClassFromString(typeName)
        return describeTypeFields(
            typeName: resolvedClass.map { String(reflecting: $0) } ?? typeName,
            assemblyName: resolvedClass.map { Bundle(for: $0).bundleIdentifier ?? "ObjectiveC" } ?? "Swift",
            namespace: namespace(fromTypeName: typeName),
            kind: resolvedClass == nil ? "type" : "class",
            baseType: resolvedClass.flatMap { class_getSuperclassName($0) },
            members: [],
            methods: []
        )
    }

    private static func describeTypeFields(
        typeName: String,
        assemblyName: String,
        namespace: String?,
        kind: String,
        baseType: String?,
        members: [JSONValue],
        methods: [JSONValue]
    ) -> JSONValue {
        .object([
            "typeName": .string(typeName),
            "assemblyName": .string(assemblyName),
            "namespace": namespace.map(JSONValue.string) ?? .null,
            "kind": .string(kind),
            "baseType": baseType.map(JSONValue.string) ?? .null,
            "interfaces": .array([]),
            "genericArity": .integer(0),
            "memberVisibility": .string("PublicOnly"),
            "members": .array(members),
            "methods": .array(methods),
            "capturedAtUtc": .string(AnsightClock.isoNow()),
        ])
    }

    private static func readSegment(_ segment: String, from value: Any) -> Any? {
        let parsed = parseSegment(segment)
        let base: Any? = parsed.name.isEmpty ? value : readNamedMember(parsed.name, from: value)
        guard let base else {
            return nil
        }

        if let index = parsed.index {
            return indexedValue(base, index: index)
        }

        return base
    }

    private static func readNamedMember(_ name: String, from value: Any) -> Any? {
        if let object = value as? [String: Any] {
            return object[name]
        }

        if let object = value as? NSDictionary {
            return object[name]
        }

        if case .object(let object) = value as? JSONValue {
            return object[name]
        }

        return namedChildren(of: value).first { $0.name == name }?.value
    }

    private static func indexedValue(_ value: Any, index: Int) -> Any? {
        guard index >= 0 else {
            return nil
        }

        if let array = value as? NSArray {
            return index < array.count ? array[index] : nil
        }

        let children = Array(Mirror(reflecting: value).children)
        return index < children.count ? children[index].value : nil
    }

    private static func namedChildren(of value: Any) -> [(name: String, value: Any)] {
        var result: [(String, Any)] = []
        var mirror: Mirror? = Mirror(reflecting: value)
        while let current = mirror {
            for child in current.children {
                guard let label = child.label, !label.isEmpty else {
                    continue
                }

                result.append((label, child.value))
            }
            mirror = current.superclassMirror
        }
        return result
    }

    private static func methodDescriptors(for value: Any) -> [JSONValue] {
        var result: [JSONValue] = []
        if value is AnsightReflectionInvokableRoot {
            result.append(.object([
                "name": .string("*"),
                "signature": .string("*(JSONValue...)"),
                "declaringType": .string(typeName(of: value)),
                "returnType": .string("JSONValue"),
                "parameterTypes": .array([.string("JSONValue...")]),
                "visibility": .string("public"),
                "invokable": .bool(true),
            ]))
        }
        return result
    }

    private static func replacementValue(_ arguments: [String: String]) throws -> JSONValue {
        if let valueJson = arguments["valueJson"]?.trimmedNonEmpty {
            return try decodeJSONValue(valueJson)
        }
        if let value = arguments["value"] {
            return .string(value)
        }
        throw AnsightReflectionToolError.invalidArgument("valueJson is required.")
    }

    private static func methodArguments(_ arguments: [String: String]) throws -> [JSONValue] {
        guard let argumentsJson = arguments["argumentsJson"]?.trimmedNonEmpty else {
            return []
        }

        let decoded = try decodeJSONValue(argumentsJson)
        guard case .array(let values) = decoded else {
            throw AnsightReflectionToolError.invalidArgument("argumentsJson must be a JSON array.")
        }
        return values
    }

    private static func decodeJSONValue(_ text: String) throws -> JSONValue {
        guard let data = text.data(using: .utf8) else {
            throw AnsightReflectionToolError.invalidArgument("JSON value must be valid UTF-8.")
        }
        return try JSONDecoder().decode(JSONValue.self, from: data)
    }

    private static func required(_ arguments: [String: String], keys: [String], name: String) throws -> String {
        for key in keys {
            if let value = arguments[key]?.trimmedNonEmpty {
                return value
            }
        }
        throw AnsightReflectionToolError.invalidArgument("\(name) is required.")
    }

    private static func integer(
        _ arguments: [String: String],
        key: String,
        defaultValue: Int,
        minimum: Int,
        maximum: Int
    ) -> Int {
        guard let rawValue = arguments[key]?.trimmedNonEmpty,
              let value = Int(rawValue) else {
            return defaultValue
        }
        return min(max(value, minimum), maximum)
    }

    private static func parseSegment(_ segment: String) -> (name: String, index: Int?) {
        guard let bracket = segment.firstIndex(of: "["),
              segment.last == "]" else {
            return (segment, nil)
        }

        let name = String(segment[..<bracket])
        let indexText = segment[segment.index(after: bracket)..<segment.index(before: segment.endIndex)]
        return (name, Int(indexText))
    }

    private static func joinPath(_ path: String?, _ segment: String) -> String {
        guard let path, !path.isEmpty else {
            return segment
        }
        if segment.hasPrefix("[") {
            return path + segment
        }
        return path + "." + segment
    }

    private static func unwrapOptional(_ value: Any?) -> Any? {
        guard let value else {
            return nil
        }

        let mirror = Mirror(reflecting: value)
        guard mirror.displayStyle == .optional else {
            return value
        }

        return mirror.children.first?.value
    }

    private static func isScalar(_ value: Any) -> Bool {
        scalarJSONValue(value) != nil
    }

    private static func scalarJSONValue(_ value: Any) -> JSONValue? {
        switch value {
        case let value as String:
            return .string(value)
        case let value as Bool:
            return .bool(value)
        case let value as Int:
            return .integer(Int64(value))
        case let value as Int8:
            return .integer(Int64(value))
        case let value as Int16:
            return .integer(Int64(value))
        case let value as Int32:
            return .integer(Int64(value))
        case let value as Int64:
            return .integer(value)
        case let value as UInt:
            return .integer(Int64(value))
        case let value as UInt8:
            return .integer(Int64(value))
        case let value as UInt16:
            return .integer(Int64(value))
        case let value as UInt32:
            return .integer(Int64(value))
        case let value as Float:
            return .number(Double(value))
        case let value as Double:
            return .number(value)
        case let value as NSNumber:
            if CFGetTypeID(value) == CFBooleanGetTypeID() {
                return .bool(value.boolValue)
            }
            let doubleValue = value.doubleValue
            let int64Value = value.int64Value
            return doubleValue.rounded() == doubleValue ? .integer(int64Value) : .number(doubleValue)
        default:
            return nil
        }
    }

    private static func kind(of value: Any) -> String {
        if isScalar(value) {
            return "scalar"
        }

        switch Mirror(reflecting: value).displayStyle {
        case .collection, .set, .dictionary:
            return "collection"
        case .struct:
            return "struct"
        case .class:
            return "object"
        case .enum:
            return "enum"
        case .tuple:
            return "tuple"
        case .optional:
            return "optional"
        default:
            return "object"
        }
    }

    private static func preview(_ value: Any) -> String? {
        let text = String(describing: value)
        return text.count <= 160 ? text : String(text.prefix(157)) + "..."
    }

    private static func typeName(of value: Any) -> String {
        String(reflecting: Swift.type(of: value))
    }

    private static func moduleName(of value: Any) -> String {
        namespace(fromTypeName: typeName(of: value)) ?? "Swift"
    }

    private static func namespace(of value: Any) -> String? {
        namespace(fromTypeName: typeName(of: value))
    }

    private static func namespace(fromTypeName typeName: String) -> String? {
        guard let dot = typeName.lastIndex(of: ".") else {
            return nil
        }
        return String(typeName[..<dot])
    }

    private static func superclassName(of value: Any) -> String? {
        Mirror(reflecting: value).superclassMirror.map { String(reflecting: $0.subjectType) }
    }

    private static func class_getSuperclassName(_ cls: AnyClass) -> String? {
        guard let superclass = cls.superclass() else {
            return nil
        }
        return String(reflecting: superclass)
    }

    private struct ResolvedReflectionRoot {
        let id: String
        let metadata: AnsightReflectionRootMetadata
        let referenceType: AnsightReflectionRootReferenceType
        let value: Any?
        let resolutionError: String?
    }

    private struct ReflectionTarget {
        let root: ResolvedReflectionRoot
        let path: String?
        let value: Any
    }
}

private extension String {
    var trimmedNonEmpty: String? {
        let trimmed = trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }
}
