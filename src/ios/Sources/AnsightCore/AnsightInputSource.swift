import Foundation

#if canImport(ObjectiveC)
/// Optional input providers can expose this label without making capture depend on them.
/// This only reads metadata; it does not register a provider or replace any input methods.
@MainActor
enum AnsightInputSource {
    static func source(for object: NSObject) -> String? {
        let selector = NSSelectorFromString("ansightInputSource")
        guard object.responds(to: selector) else { return nil }
        return object.perform(selector)?.takeUnretainedValue() as? String
    }
}
#endif
