import Foundation

#if canImport(ObjectiveC)
/// Optional input providers can expose this label without making capture depend on them.
/// Capture does not register a provider or replace any input methods.
@MainActor
enum AnsightInputSource {
    static func prepareCoalescedSample(_ sample: NSObject, from source: NSObject) {
        let selector = NSSelectorFromString("ansightBindCoalescedTouch:")
        guard source.responds(to: selector) else { return }
        source.perform(selector, with: sample)
    }

    static func source(for object: NSObject) -> String? {
        let selector = NSSelectorFromString("ansightInputSource")
        guard object.responds(to: selector) else { return nil }
        return object.perform(selector)?.takeUnretainedValue() as? String
    }
}
#endif
