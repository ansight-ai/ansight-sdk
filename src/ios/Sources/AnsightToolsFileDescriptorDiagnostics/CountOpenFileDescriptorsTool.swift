import AnsightCore
import Foundation

public final class CountOpenFileDescriptorsTool: AnsightTool {
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
            id: AnsightFileDescriptorDiagnosticsToolIds.countOpen,
            name: "Count Open File Descriptors",
            description: "Counts live file descriptors owned by the current app process without returning descriptor details.",
            category: "file_descriptors",
            policy: .read,
            keywords: "file descriptors handles count limits diagnostics",
            argumentsSchema: AnsightFileDescriptorDiagnosticsToolSchemas.countOpenArguments,
            resultSchema: AnsightFileDescriptorDiagnosticsToolSchemas.countOpenResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let snapshot = try collector.count(options: options)
            guard snapshot.scanComplete else {
                return .failure(
                    "The descriptor count exceeded the configured scan limit of \(snapshot.scannedDescriptorLimit).",
                    errorCode: "file_descriptor_scan_incomplete"
                )
            }
            return .success(.object([
                "count": .integer(Int64(snapshot.count)),
            ]))
        } catch {
            return .failure(error.localizedDescription, errorCode: "file_descriptor_count_failed")
        }
    }
}
