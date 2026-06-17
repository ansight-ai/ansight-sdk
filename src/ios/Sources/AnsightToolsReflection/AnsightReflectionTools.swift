import AnsightCore
import Foundation

public enum AnsightReflectionTools {
    public static func tools(
        options: AnsightReflectionToolsOptions = .default,
        runtime: AnsightRuntime = .shared
    ) -> [any AnsightTool] {
        [
            ListReflectionRootsTool(options: options, runtime: runtime),
            InspectObjectTool(options: options, runtime: runtime),
            DescribeTypeTool(options: options, runtime: runtime),
            SetMemberValueTool(options: options, runtime: runtime),
            InvokeMethodTool(options: options, runtime: runtime),
        ]
    }
}
