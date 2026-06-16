import AnsightCore
import Foundation

public enum AnsightFileSystemTools {
    public static func tools(options: AnsightFileSystemToolsOptions = .default) -> [any AnsightTool] {
        [
            ListDirectoryTool(options: options),
            ReadFileTool(options: options),
            GetFileChecksumTool(options: options),
            DownloadFileTool(options: options),
            BeginBinaryDownloadTool(options: options),
            PushFileTool(options: options),
            CopyFileTool(options: options),
            MoveFileTool(options: options),
            DeleteFileTool(options: options),
        ]
    }
}
