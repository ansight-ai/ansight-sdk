import AnsightKit
import Foundation

public enum AnsightVisualTreeTools {
    public static func tools(runtime: AnsightRuntime = .shared) -> [any AnsightTool] {
        [
            GetVisualTreeTool(),
            GetScreenshotTool(runtime: runtime),
            InspectNodeTool(),
            ShowOverlayTool(),
            GetOverlayTool(),
            QueryOverlaysTool(),
            UpdateOverlayTool(),
            RemoveOverlayTool(),
            ClearOverlaysTool(),
        ]
    }
}
