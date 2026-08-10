import AnsightCore
import Foundation

public enum AnsightVisualTreeProviderRegistry {
    public static let nativeSource = "native"

    private static let lock = NSLock()
    nonisolated(unsafe) private static var providers: [String: any AnsightVisualTreeProvider] = [
        nativeSource: AnsightNativeVisualTreeProvider(),
    ]

    public static func register(_ provider: any AnsightVisualTreeProvider, replaceExisting: Bool = true) throws {
        let source = try normalizedSource(provider.source)
        lock.lock()
        defer { lock.unlock() }

        if !replaceExisting, providers[source] != nil {
            throw AnsightVisualTreeToolError.invalidArgument("A visual tree provider for source '\(source)' is already registered.")
        }

        providers[source] = provider
    }

    public static func provider(for source: String?) -> (any AnsightVisualTreeProvider)? {
        let normalized = normalizedSourceOrDefault(source)
        lock.lock()
        defer { lock.unlock() }
        return providers[normalized]
    }

    public static func registeredSources() -> [String] {
        lock.lock()
        defer { lock.unlock() }
        return providers.keys.sorted()
    }

    public static func registeredProviders() -> [any AnsightVisualTreeProvider] {
        lock.lock()
        defer { lock.unlock() }
        return providers.keys.sorted().compactMap { providers[$0] }
    }

    internal static func normalizedSourceOrDefault(_ source: String?) -> String {
        let normalized = source?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        return normalized?.isEmpty == false ? normalized! : nativeSource
    }

    private static func normalizedSource(_ source: String) throws -> String {
        let normalized = source.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !normalized.isEmpty else {
            throw AnsightVisualTreeToolError.invalidArgument("Visual tree provider source must not be blank.")
        }

        guard normalized.count <= 64 else {
            throw AnsightVisualTreeToolError.invalidArgument("Visual tree provider source must be at most 64 characters.")
        }

        return normalized
    }
}
