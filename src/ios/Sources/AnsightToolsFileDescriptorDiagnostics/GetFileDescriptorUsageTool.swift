import AnsightCore
import Foundation

public final class GetFileDescriptorUsageTool: AnsightTool {
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
            id: AnsightFileDescriptorDiagnosticsToolIds.getUsage,
            name: "Get File Descriptor Usage",
            description: "Reports current open descriptor usage against the process soft and hard limits.",
            category: "file_descriptors",
            policy: .read,
            keywords: "file descriptors handles usage limits exhaustion diagnostics",
            argumentsSchema: AnsightFileDescriptorDiagnosticsToolSchemas.getUsageArguments,
            resultSchema: AnsightFileDescriptorDiagnosticsToolSchemas.getUsageResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let snapshot = try collector.count(options: options)
            let openCount = UInt64(snapshot.count)
            let softLimit = snapshot.limits.softLimit
            let available = snapshot.scanComplete && softLimit != nil
                ? softLimit.map { $0 > openCount ? $0 - openCount : 0 }
                : nil
            let utilization = snapshot.scanComplete && softLimit != nil && softLimit != 0
                ? softLimit.map { Double(openCount) / Double($0) * 100.0 }
                : nil

            var result = AnsightFileDescriptorDiagnosticsToolSupport.snapshotMetadata(snapshot)
            result["openCount"] = .integer(Int64(openCount))
            result["softLimit"] = AnsightFileDescriptorDiagnosticsToolSupport.optionalInt64(softLimit)
            result["hardLimit"] = AnsightFileDescriptorDiagnosticsToolSupport.optionalInt64(snapshot.limits.hardLimit)
            result["hardLimitUnlimited"] = .bool(snapshot.limits.hardLimitUnlimited)
            result["availableBeforeSoftLimit"] = AnsightFileDescriptorDiagnosticsToolSupport.optionalInt64(available)
            result["utilizationPercent"] = utilization.map(JSONValue.number) ?? .null
            return .success(.object(result))
        } catch {
            return .failure(error.localizedDescription, errorCode: "file_descriptor_usage_failed")
        }
    }
}
