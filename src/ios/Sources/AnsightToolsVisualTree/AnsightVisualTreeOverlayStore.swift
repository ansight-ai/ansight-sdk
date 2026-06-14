import Foundation

internal final class AnsightVisualTreeOverlayStore: @unchecked Sendable {
    static let shared = AnsightVisualTreeOverlayStore()

    private let lock = NSLock()
    private var overlays: [String: AnsightVisualTreeOverlay] = [:]

    private init() {}

    func get(_ id: String) -> AnsightVisualTreeOverlay? {
        lock.withLock { overlays[id] }
    }

    func set(_ overlay: AnsightVisualTreeOverlay) -> AnsightVisualTreeOverlay? {
        lock.withLock {
            let existing = overlays[overlay.id]
            overlays[overlay.id] = overlay
            return existing
        }
    }

    func remove(_ id: String) -> AnsightVisualTreeOverlay? {
        lock.withLock {
            overlays.removeValue(forKey: id)
        }
    }

    func all() -> [AnsightVisualTreeOverlay] {
        lock.withLock { Array(overlays.values) }
    }

    func removeExpired(now: Date) -> [AnsightVisualTreeOverlay] {
        lock.withLock {
            let expired = overlays.values.filter { $0.isExpired(now) }
            for overlay in expired {
                overlays.removeValue(forKey: overlay.id)
            }
            return expired
        }
    }
}
