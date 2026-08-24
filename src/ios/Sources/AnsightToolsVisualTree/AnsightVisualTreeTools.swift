import AnsightCore
import Foundation

public enum AnsightVisualTreeTools {
    public static func tools(runtime: AnsightRuntime = .shared) -> [any AnsightTool] {
        AnsightSessionVisualTreeCaptureRegistry.setProvider {
            AnsightVisualTreeProviderRegistry.registeredProviders().compactMap { provider in
                AnsightVisualTreeSnapshotStore.capture(source: provider.source, arguments: [
                    "includeBounds": "true",
                    "includeComputedStyles": "true",
                    "maxDepth": "40",
                    "maxNodes": "2000",
                ]).result
            }
        }

        return [
            GetVisualTreeTool(),
            GetScreenshotTool(runtime: runtime),
            InspectNodeTool(),
            QueryNodesTool(),
            PerformActionTool(),
            WaitForUIConditionTool(),
            ShowOverlayTool(),
            GetOverlayTool(),
            QueryOverlaysTool(),
            UpdateOverlayTool(),
            RemoveOverlayTool(),
            ClearOverlaysTool(),
        ]
    }
}
