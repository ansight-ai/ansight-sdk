import Foundation

public typealias HostConnectionStatusListener = @Sendable (HostConnectionStatus, HostConnectionCapabilities) -> Void

public final class HostConnectionStatusSubscription: @unchecked Sendable {
    private let lock = NSLock()
    private var removeAction: (() -> Void)?

    init(removeAction: @escaping () -> Void) {
        self.removeAction = removeAction
    }

    public func remove() {
        let action = lock.withLock { () -> (() -> Void)? in
            let action = removeAction
            removeAction = nil
            return action
        }

        action?()
    }

    deinit {
        remove()
    }
}
