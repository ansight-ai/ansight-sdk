#if canImport(StoreKit)
import Foundation
import StoreKit

/// Reads StoreKit 2 without owning the app's purchase or transaction-finishing workflow.
@available(iOS 15.0, macOS 12.0, *)
public enum PurchaseStoreKit {
    /// Refresh known product IDs. Call after purchase callbacks and at app activation.
    /// No credentials, raw signed transactions, or transaction IDs leave this adapter.
    public static func refresh(productIds: [String], diagnostics: PurchaseDiagnostics = .shared) async throws {
        try diagnostics.ensureInteropAllowed()
        guard !productIds.isEmpty, productIds.count <= 100,
              productIds.allSatisfy({ !$0.isEmpty && $0.count <= 256 }) else { throw PurchaseDiagnosticsError.invalidArguments }
        let generation = diagnostics.captureGeneration()
        var observations: [PurchaseObservation] = []
        var productObservations: [PurchaseProduct] = []
        let products = try await Product.products(for: productIds)
        for id in Set(productIds).sorted() {
            if Task.isCancelled { throw CancellationError() }
            let product = products.first { $0.id == id }
            productObservations.append(PurchaseProduct(productId: id, available: product != nil,
                displayPrice: product?.displayPrice, price: product.map { NSDecimalNumber(decimal: $0.price).stringValue },
                currencyCode: product?.priceFormatStyle.currencyCode, productType: product.map { productType($0.type) } ?? "unknown"))
            try diagnostics.ensureInteropAllowed()
            let result = await Transaction.latest(for: id)
            guard let result else {
                var missing = PurchaseObservation(productId: id, source: "store")
                missing.entitled = false
                observations.append(missing)
                continue
            }
            let transaction: Transaction
            var observation = PurchaseObservation(productId: id, source: "store")
            switch result {
            case .verified(let value): transaction = value; observation.verification = "verified"
            case .unverified(let value, _): transaction = value; observation.verification = "unverified"
            }
            if #available(iOS 16.0, macOS 13.0, *) {
                observation.environment = transaction.environment == .xcode ? "xcode" : transaction.environment == .sandbox ? "sandbox" : transaction.environment == .production ? "production" : "unknown"
            }
            guard observation.environment != "production" else { throw PurchaseDiagnosticsError.environmentNotAllowed }
            observation.transactionRef = try diagnostics.createTransactionReference(String(transaction.id))
            observation.productType = productType(transaction.productType)
            observation.expiresAt = transaction.expirationDate.map { Int64($0.timeIntervalSince1970 * 1000) }
            let revoked = transaction.revocationDate != nil
            let expired = transaction.expirationDate.map { $0 <= Date() } ?? false
            observation.state = revoked ? "revoked" : expired ? "expired" : "purchased"
            // Consumable delivery and non-renewing expiry are application-owned.
            if transaction.productType == .consumable || transaction.productType == .nonRenewable {
                observation.entitled = nil
            } else { observation.entitled = !revoked && !expired }
            if let subscription = product?.subscription {
                try diagnostics.ensureInteropAllowed()
                for status in try await subscription.status {
                    let statusTransaction: Transaction
                    switch status.transaction {
                    case .verified(let value): statusTransaction = value
                    case .unverified(let value, _): statusTransaction = value
                    }
                    guard statusTransaction.id == transaction.id else { continue }
                    if status.state == .inGracePeriod {
                        observation.state = "gracePeriod"
                        switch status.renewalInfo {
                        case .verified(let renewal):
                            observation.gracePeriodExpiresAt = renewal.gracePeriodExpirationDate.map { Int64($0.timeIntervalSince1970 * 1000) }
                            observation.entitled = observation.gracePeriodExpiresAt.map { $0 > PurchaseDiagnostics.now() }
                        case .unverified:
                            observation.verification = "unverified"
                            observation.entitled = nil
                        }
                    } else if status.state == .inBillingRetryPeriod {
                        observation.state = "billingRetry"; observation.entitled = false
                    } else if status.state == .revoked {
                        observation.state = "revoked"; observation.entitled = false
                    } else if status.state == .expired {
                        observation.state = "expired"; observation.entitled = false
                    }
                }
            }
            observations.append(observation)
        }
        try Task.checkCancellation()
        try diagnostics.recordStoreKitBatch(observations, products: productObservations, generation: generation)
    }

    private static func productType(_ type: Product.ProductType) -> String {
        switch type {
        case .consumable: return "consumable"
        case .nonConsumable: return "nonConsumable"
        case .autoRenewable: return "subscription"
        case .nonRenewable: return "nonRenewingSubscription"
        default: return "unknown"
        }
    }
}
#endif
