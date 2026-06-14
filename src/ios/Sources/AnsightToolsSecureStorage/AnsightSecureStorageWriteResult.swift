import Foundation

internal struct AnsightSecureStorageWriteResult: Sendable, Equatable {
    let store: String
    let key: String
    let updated: Bool
}
