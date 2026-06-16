import Foundation

struct RuntimeLifecycleChange: Sendable, Equatable {
    static let unchanged = RuntimeLifecycleChange(didChange: false, shouldSendAppState: false)

    let didChange: Bool
    let shouldSendAppState: Bool
}
