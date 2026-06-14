import Foundation

public final class AnsightPreferencesToolOptionsBuilder {
    private var defaultStore: String?
    private var allowedStores: [String] = []
    private var allowedKeys: [String] = []
    private var allowedKeyPrefixes: [String] = []

    public init() {}

    @discardableResult
    public func withDefaultStore(_ store: String?) -> AnsightPreferencesToolOptionsBuilder {
        defaultStore = store
        return self
    }

    @discardableResult
    public func allowStore(_ store: String) -> AnsightPreferencesToolOptionsBuilder {
        allowedStores.append(store)
        return self
    }

    @discardableResult
    public func allowStores(_ stores: [String]) -> AnsightPreferencesToolOptionsBuilder {
        allowedStores.append(contentsOf: stores)
        return self
    }

    @discardableResult
    public func allowKey(_ key: String) -> AnsightPreferencesToolOptionsBuilder {
        allowedKeys.append(key)
        return self
    }

    @discardableResult
    public func allowKeys(_ keys: [String]) -> AnsightPreferencesToolOptionsBuilder {
        allowedKeys.append(contentsOf: keys)
        return self
    }

    @discardableResult
    public func allowKeyPrefix(_ keyPrefix: String) -> AnsightPreferencesToolOptionsBuilder {
        allowedKeyPrefixes.append(keyPrefix)
        return self
    }

    @discardableResult
    public func allowKeyPrefixes(_ keyPrefixes: [String]) -> AnsightPreferencesToolOptionsBuilder {
        allowedKeyPrefixes.append(contentsOf: keyPrefixes)
        return self
    }

    public func build() -> AnsightPreferencesToolOptions {
        AnsightPreferencesToolOptions(
            defaultStore: defaultStore,
            allowedStores: allowedStores,
            allowedKeys: allowedKeys,
            allowedKeyPrefixes: allowedKeyPrefixes
        )
    }
}
