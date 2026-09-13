package ai.ansight.purchases.googleplay

import ai.ansight.runtime.purchases.*
import com.android.billingclient.api.Purchase
import com.android.billingclient.api.ProductDetails
import java.math.BigDecimal

/** Call from your existing billing callbacks. Does not create a BillingClient or consume purchases. */
class GooglePlayPurchaseObserver @JvmOverloads constructor(private val diagnostics: PurchaseDiagnostics = PurchaseDiagnostics.shared) {
    @JvmOverloads fun record(purchase: Purchase, productType: String = "unknown") {
        diagnostics.ensureInteropAllowed()
        val state = when (purchase.purchaseState) {
            Purchase.PurchaseState.PURCHASED -> "purchased"
            Purchase.PurchaseState.PENDING -> "pending"
            else -> "unknown"
        }
        for (id in purchase.products) diagnostics.record(PurchaseObservation(
            productId = id, source = "store", state = state,
            // PURCHASED is not cryptographic/backend verification.
            entitled = if (state == "unknown") null else state == "purchased" && !purchase.isSuspended,
            acknowledged = purchase.isAcknowledged, productType = productType,
            transactionRef = diagnostics.createTransactionReference(purchase.purchaseToken),
        ))
    }

    /** Supply a selected offer index for subscriptions; otherwise no price is inferred. */
    @JvmOverloads fun recordProduct(details: ProductDetails, subscriptionOfferIndex: Int? = null) {
        diagnostics.ensureInteropAllowed()
        val offer = subscriptionOfferIndex?.let { index ->
            require(index >= 0 && index < (details.subscriptionOfferDetails?.size ?: 0))
            details.subscriptionOfferDetails!![index]
        }
        val phase = offer?.pricingPhases?.pricingPhaseList?.lastOrNull()
        val oneTime = details.oneTimePurchaseOfferDetails
        val micros = phase?.priceAmountMicros ?: oneTime?.priceAmountMicros
        diagnostics.recordProduct(PurchaseProduct(productId = details.productId, available = true,
            displayPrice = phase?.formattedPrice ?: oneTime?.formattedPrice,
            price = micros?.let { BigDecimal.valueOf(it, 6).toPlainString() },
            currencyCode = phase?.priceCurrencyCode ?: oneTime?.priceCurrencyCode,
            productType = if (details.productType == "subs") "subscription" else "unknown"))
    }
}
