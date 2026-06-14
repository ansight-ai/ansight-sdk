import Foundation

internal struct AnsightSecureStorageValueResult: Sendable, Equatable {
    let store: String
    let key: String
    let exists: Bool
    let value: String?
}
