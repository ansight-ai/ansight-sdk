/// Uses the native compilation target without contacting StoreKit.
internal enum PurchaseEnvironment {
    static func isAllowed() -> Bool {
        #if targetEnvironment(simulator)
        return true
        #else
        return false
        #endif
    }
}
