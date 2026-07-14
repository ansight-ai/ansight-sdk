import AnsightCore
import Foundation

public final class ListOpenFileDescriptorsTool: AnsightTool {
    private let options: AnsightFileDescriptorDiagnosticsOptions
    private let collector: any AnsightFileDescriptorCollecting

    public convenience init(options: AnsightFileDescriptorDiagnosticsOptions = .default) {
        self.init(options: options, collector: AnsightSystemFileDescriptorCollector())
    }

    internal init(
        options: AnsightFileDescriptorDiagnosticsOptions,
        collector: any AnsightFileDescriptorCollecting
    ) {
        self.options = options
        self.collector = collector
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightFileDescriptorDiagnosticsToolIds.listOpen,
            name: "List Open File Descriptors",
            description: "Lists live file descriptors owned by the current app process.",
            category: "file_descriptors",
            scope: AnsightToolScope.read.rawValue,
            keywords: "file descriptors handles open files sockets pipes diagnostics",
            security: AnsightFileDescriptorDiagnosticsToolSecurityProfiles.listOpen,
            argumentsSchema: AnsightFileDescriptorDiagnosticsToolSchemas.listOpenArguments,
            resultSchema: AnsightFileDescriptorDiagnosticsToolSchemas.listOpenResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let snapshot = try collector.snapshot(options: options)
            let maximum = try AnsightFileDescriptorDiagnosticsToolSupport.integer(
                arguments,
                key: "maxEntries",
                defaultValue: min(256, options.maximumReturnedDescriptors),
                minimum: 1,
                maximum: options.maximumReturnedDescriptors
            )
            let kindFilter = try parseKind(arguments["kind"])
            let targetFilter = arguments["targetContains"]?
                .trimmingCharacters(in: .whitespacesAndNewlines)
                .lowercased()
                .nilIfEmpty

            let matching = snapshot.descriptors.filter { descriptor in
                if let kindFilter, descriptor.kind != kindFilter {
                    return false
                }
                if let targetFilter, descriptor.target?.lowercased().contains(targetFilter) != true {
                    return false
                }
                return true
            }
            let returned = Array(matching.prefix(maximum))
            var result = AnsightFileDescriptorDiagnosticsToolSupport.snapshotMetadata(snapshot)
            result["count"] = .integer(Int64(snapshot.descriptors.count))
            result["matchedCount"] = .integer(Int64(matching.count))
            result["returnedCount"] = .integer(Int64(returned.count))
            result["descriptors"] = .array(returned.map(\.jsonValue))
            result["truncated"] = .bool(returned.count < matching.count)
            return .success(.object(result))
        } catch {
            return .failure(error.localizedDescription, errorCode: "file_descriptor_list_failed")
        }
    }

    private func parseKind(_ rawValue: String?) throws -> AnsightFileDescriptorKind? {
        guard let value = rawValue?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased().nilIfEmpty else {
            return nil
        }
        guard let kind = AnsightFileDescriptorKind(rawValue: value) else {
            throw AnsightFileDescriptorDiagnosticsError.invalidArgument("Unknown file descriptor kind '\(value)'.")
        }
        return kind
    }
}

private extension String {
    var nilIfEmpty: String? {
        isEmpty ? nil : self
    }
}
