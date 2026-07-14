import AnsightCore
import Foundation

public enum AnsightFileDescriptorDiagnosticsTools {
    public static func tools(
        options: AnsightFileDescriptorDiagnosticsOptions = .default
    ) -> [any AnsightTool] {
        [
            ListOpenFileDescriptorsTool(options: options),
            CountOpenFileDescriptorsTool(options: options),
            InspectFileDescriptorTool(options: options),
            GetFileDescriptorUsageTool(options: options),
        ]
    }
}
