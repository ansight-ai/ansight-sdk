import Foundation

internal protocol AnsightPreferencesBackend {
    func listKeys(store: String?) throws -> AnsightPreferenceListKeysResult
    func getValue(store: String?, key: String) throws -> AnsightPreferenceValueResult
    func setValue(store: String?, key: String, valueKind: AnsightPreferenceValueKind, value: String) throws -> AnsightPreferenceWriteResult
    func removeKey(store: String?, key: String) throws -> AnsightPreferenceRemoveResult
}
