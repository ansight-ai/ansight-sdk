import Foundation

internal struct AnsightSecureStorageRemoveResult: Sendable, Equatable {
    let store: String
    let key: String
    let removed: Bool
}
