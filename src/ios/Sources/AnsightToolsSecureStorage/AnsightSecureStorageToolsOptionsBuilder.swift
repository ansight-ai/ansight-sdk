import Foundation

public final class AnsightSecureStorageToolsOptionsBuilder {
    private var appleService: String?
    private var allowedKeys: Set<String> = []
    private var allowedKeyPrefixes: Set<String> = []

    public init() {}

    @discardableResult
    public func withStorageIdentifier(_ identifier: String) -> AnsightSecureStorageToolsOptionsBuilder {
        let value = identifier.trimmingCharacters(in: .whitespacesAndNewlines)
        if !value.isEmpty {
            appleService = value
        }

        return self
    }

    @discardableResult
    public func withAppleService(_ service: String) -> AnsightSecureStorageToolsOptionsBuilder {
        withStorageIdentifier(service)
    }

    @discardableResult
    public func allowKey(_ key: String) -> AnsightSecureStorageToolsOptionsBuilder {
        let value = key.trimmingCharacters(in: .whitespacesAndNewlines)
        if !value.isEmpty {
            allowedKeys.insert(value)
        }

        return self
    }

    @discardableResult
    public func allowKeyPrefix(_ keyPrefix: String) -> AnsightSecureStorageToolsOptionsBuilder {
        let value = keyPrefix.trimmingCharacters(in: .whitespacesAndNewlines)
        if !value.isEmpty {
            allowedKeyPrefixes.insert(value)
        }

        return self
    }

    public func build() -> AnsightSecureStorageToolsOptions {
        AnsightSecureStorageToolsOptions(
            appleService: appleService,
            allowedKeys: allowedKeys.sorted(),
            allowedKeyPrefixes: allowedKeyPrefixes.sorted()
        )
    }
}
