import Foundation

internal protocol AnsightSecureStorageBackend: Sendable {
    func getValue(key: String) throws -> AnsightSecureStorageValueResult
    func setValue(key: String, value: String) throws -> AnsightSecureStorageWriteResult
    func removeKey(key: String) throws -> AnsightSecureStorageRemoveResult
}
