import Foundation

/// Objective-C facade over the base SDK purchase contract.
@objc(ANSPurchaseDiagnostics)
public final class ANSPurchaseDiagnostics: NSObject {
    @objc(command:completion:)
    public static func command(_ json: String, completion: @escaping @Sendable (String?, String?) -> Void) {
        Task {
            do { completion(try await PurchaseDiagnostics.shared.commandAsync(json), nil) }
            catch PurchaseDiagnosticsError.environmentNotAllowed { completion(nil, PurchaseDiagnosticsError.environmentErrorCode) }
            catch { completion(nil, "purchases_command_failed") }
        }
    }
}
