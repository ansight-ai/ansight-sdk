import Foundation
import Security

internal struct AnsightKeychainSecureStorageBackend: AnsightSecureStorageBackend {
    private let service: String

    init(options: AnsightSecureStorageToolsOptions) {
        service = AnsightSecureStorageSupport.resolveAppleService(options: options)
    }

    func getValue(key: String) throws -> AnsightSecureStorageValueResult {
        var query = baseQuery(key: key)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        switch status {
        case errSecSuccess:
            guard let data = item as? Data,
                  let value = String(data: data, encoding: .utf8) else {
                throw AnsightSecureStorageToolError.operationFailed("Unable to decode Keychain item '\(key)' as UTF-8.")
            }

            return AnsightSecureStorageValueResult(store: service, key: key, exists: true, value: value)
        case errSecItemNotFound:
            return AnsightSecureStorageValueResult(store: service, key: key, exists: false, value: nil)
        default:
            throw AnsightSecureStorageToolError.operationFailed("Unable to access Keychain item '\(key)'. Status: \(status).")
        }
    }

    func setValue(key: String, value: String) throws -> AnsightSecureStorageWriteResult {
        let deleteStatus = SecItemDelete(baseQuery(key: key) as CFDictionary)
        if deleteStatus != errSecSuccess && deleteStatus != errSecItemNotFound {
            throw AnsightSecureStorageToolError.operationFailed("Unable to replace Keychain item '\(key)'. Status: \(deleteStatus).")
        }

        var item = baseQuery(key: key)
        item[kSecAttrLabel as String] = key
        item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        item[kSecValueData as String] = Data(value.utf8)

        let addStatus = SecItemAdd(item as CFDictionary, nil)
        if addStatus != errSecSuccess {
            throw AnsightSecureStorageToolError.operationFailed("Unable to store Keychain item '\(key)'. Status: \(addStatus).")
        }

        return AnsightSecureStorageWriteResult(store: service, key: key, updated: true)
    }

    func removeKey(key: String) throws -> AnsightSecureStorageRemoveResult {
        let status = SecItemDelete(baseQuery(key: key) as CFDictionary)
        switch status {
        case errSecSuccess:
            return AnsightSecureStorageRemoveResult(store: service, key: key, removed: true)
        case errSecItemNotFound:
            return AnsightSecureStorageRemoveResult(store: service, key: key, removed: false)
        default:
            throw AnsightSecureStorageToolError.operationFailed("Unable to remove Keychain item '\(key)'. Status: \(status).")
        }
    }

    private func baseQuery(key: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
        ]
    }
}
