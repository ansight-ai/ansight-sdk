import Foundation

enum AnsightTiming {
    static func now() -> TimeInterval {
        ProcessInfo.processInfo.systemUptime
    }

    static func elapsedMilliseconds(since start: TimeInterval) -> Int {
        max(0, Int(((now() - start) * 1_000).rounded()))
    }
}
