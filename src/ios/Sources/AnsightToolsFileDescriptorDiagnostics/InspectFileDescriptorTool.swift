import AnsightCore
import Foundation

public final class InspectFileDescriptorTool: AnsightTool {
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
            id: AnsightFileDescriptorDiagnosticsToolIds.inspect,
            name: "Inspect File Descriptor",
            description: "Returns metadata for one live file descriptor in the current app process.",
            category: "file_descriptors",
            policy: .read,
            keywords: "file descriptor handle inspect path socket pipe diagnostics",
            argumentsSchema: AnsightFileDescriptorDiagnosticsToolSchemas.inspectArguments,
            resultSchema: AnsightFileDescriptorDiagnosticsToolSchemas.inspectResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let descriptor = try AnsightFileDescriptorDiagnosticsToolSupport.integer(
                arguments,
                key: "descriptor",
                minimum: 0,
                maximum: Int(Int32.max)
            )
            guard let info = try collector.inspect(descriptor: descriptor, includeTarget: options.includeTargets) else {
                return .failure(
                    "File descriptor \(descriptor) is not open.",
                    errorCode: "file_descriptor_not_open"
                )
            }
            return .success(.object([
                "descriptor": info.jsonValue,
                "capturedAtUtc": .string(AnsightClock.isoNow()),
            ]))
        } catch {
            return .failure(error.localizedDescription, errorCode: "file_descriptor_inspect_failed")
        }
    }
}
