import Foundation

public struct PurchaseProduct: Sendable, Codable, Equatable {
    public let productId: String
    public let available: Bool
    public let displayPrice: String?
    public let price: String?
    public let currencyCode: String?
    public let productType: String
    public let observedAt: Int64
    public init(productId: String, available: Bool, displayPrice: String? = nil, price: String? = nil, currencyCode: String? = nil, productType: String = "unknown", observedAt: Int64 = PurchaseDiagnostics.now()) {
        self.productId = productId; self.available = available; self.displayPrice = displayPrice
        self.price = price; self.currencyCode = currencyCode; self.productType = productType; self.observedAt = observedAt
    }
}
