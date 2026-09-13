package ai.ansight.runtime.purchases

import org.json.JSONObject

data class PurchaseProduct @JvmOverloads constructor(val productId: String, val available: Boolean,
    val observedAt: Long = System.currentTimeMillis(), val displayPrice: String? = null,
    val price: String? = null, val currencyCode: String? = null, val productType: String = "unknown") {
    fun toJson(): JSONObject = JSONObject().put("productId", productId).put("available", available)
        .put("observedAt", observedAt).put("displayPrice", displayPrice ?: JSONObject.NULL)
        .put("price", price ?: JSONObject.NULL).put("currencyCode", currencyCode ?: JSONObject.NULL).put("productType", productType)
    companion object {
        fun fromJson(value: JSONObject) = PurchaseProduct(value.getString("productId"), value.getBoolean("available"),
            if (value.has("observedAt")) value.getLong("observedAt") else System.currentTimeMillis(),
            if (value.isNull("displayPrice")) null else value.getString("displayPrice"),
            if (value.isNull("price")) null else value.getString("price"),
            if (value.isNull("currencyCode")) null else value.getString("currencyCode"), value.optString("productType", "unknown"))
    }
}
