import Foundation

/// Allows a visual-tree SDK module to supply snapshots for screenshot-and-tree session capture.
public enum AnsightSessionVisualTreeCaptureRegistry {
    public typealias CaptureProvider = @Sendable () -> [JSONValue]

    private static let lock = NSLock()
    nonisolated(unsafe) private static var provider: CaptureProvider?

    public static func setProvider(_ provider: CaptureProvider?) {
        lock.lock()
        self.provider = provider
        lock.unlock()
    }

    internal static func capture() -> [JSONValue] {
        lock.lock()
        let provider = self.provider
        lock.unlock()
        return provider?() ?? []
    }
}
