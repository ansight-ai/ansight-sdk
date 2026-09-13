package ai.ansight.runtime.purchases

import ai.ansight.runtime.*
import java.security.MessageDigest
import java.util.UUID
import org.json.JSONArray
import org.json.JSONObject

/** No receipt, token, account identifier, or arbitrary metadata is retained. */
data class PurchaseObservation @JvmOverloads constructor(
    val productId: String,
    val source: String,
    val observedAt: Long = System.currentTimeMillis(),
    val state: String = "unknown",
    val verification: String = "unknown",
    val entitled: Boolean? = null,
    val deliveryCount: Int? = null,
    val finished: Boolean? = null,
    val acknowledged: Boolean? = null,
    val consumed: Boolean? = null,
    val expiresAt: Long? = null,
    val environment: String = "unknown",
    val productType: String = "unknown",
    val transactionRef: String? = null,
    val gracePeriodExpiresAt: Long? = null,
) {
    fun toJson(): JSONObject = JSONObject().put("productId", productId).put("source", source)
        .put("observedAt", observedAt).put("state", state).put("verification", verification)
        .put("entitled", entitled ?: JSONObject.NULL).put("deliveryCount", deliveryCount ?: JSONObject.NULL)
        .put("finished", finished ?: JSONObject.NULL).put("acknowledged", acknowledged ?: JSONObject.NULL)
        .put("consumed", consumed ?: JSONObject.NULL).put("expiresAt", expiresAt ?: JSONObject.NULL)
        .put("gracePeriodExpiresAt", gracePeriodExpiresAt ?: JSONObject.NULL).put("environment", environment).put("productType", productType).put("transactionRef", transactionRef ?: JSONObject.NULL)

    companion object {
        fun fromJson(value: JSONObject): PurchaseObservation = PurchaseObservation(
            value.getString("productId"), value.getString("source"),
            if (value.has("observedAt")) value.getLong("observedAt") else System.currentTimeMillis(),
            value.optString("state", "unknown"), value.optString("verification", "unknown"),
            if (value.isNull("entitled")) null else value.getBoolean("entitled"),
            if (value.isNull("deliveryCount")) null else value.getInt("deliveryCount"),
            if (value.isNull("finished")) null else value.getBoolean("finished"),
            if (value.isNull("acknowledged")) null else value.getBoolean("acknowledged"),
            if (value.isNull("consumed")) null else value.getBoolean("consumed"),
            if (value.isNull("expiresAt")) null else value.getLong("expiresAt"),
            value.optString("environment", "unknown"), value.optString("productType", "unknown"),
            if (value.isNull("transactionRef")) null else value.getString("transactionRef"),
            if (value.isNull("gracePeriodExpiresAt")) null else value.getLong("gracePeriodExpiresAt"),
        )
    }
}

/** Passive purchase evidence; reads never acknowledge, consume, or grant a purchase. */
class PurchaseDiagnostics private constructor(private val environmentAllowed: () -> Boolean) {
    constructor() : this(PurchaseEnvironment::isAllowed)
    val isInteropAllowed: Boolean get() = runCatching { environmentAllowed() }.getOrDefault(false)
    fun ensureInteropAllowed() {
        if (!isInteropAllowed) { clear(); throw PurchaseEnvironmentException() }
    }
    private val observations = mutableListOf<PurchaseObservation>()
    private var dropped = 0L
    private var referenceSalt = UUID.randomUUID().toString()
    private val products = mutableListOf<PurchaseProduct>()

    @Synchronized fun createTransactionReference(identifier: String): String {
        ensureInteropAllowed()
        require(identifier.isNotBlank() && identifier.length <= 8192)
        return MessageDigest.getInstance("SHA-256").digest((referenceSalt + identifier).toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }
    }

    @Synchronized fun record(value: PurchaseObservation) {
        ensureInteropAllowed()
        if (value.environment == "production") throw PurchaseEnvironmentException()
        require(value.productId.isNotBlank() && value.productId.length <= 256
            && value.source in listOf("store", "app", "backend")
            && value.state in listOf("unknown", "pending", "purchased", "cancelled", "expired", "revoked", "failed", "gracePeriod", "billingRetry")
            && value.verification in listOf("unknown", "verified", "unverified")
            && value.environment in listOf("unknown", "xcode", "sandbox", "production")
            && value.productType in listOf("unknown", "consumable", "nonConsumable", "subscription", "nonRenewingSubscription")
            && (value.transactionRef == null || (value.transactionRef.length == 64 && value.transactionRef.all { it in "0123456789abcdef" }))
            && value.observedAt >= 0 && (value.expiresAt ?: 0) >= 0 && (value.gracePeriodExpiresAt ?: 0) >= 0 && (value.deliveryCount ?: 0) >= 0) { "Invalid purchase observation." }
        if (observations.size == 256) { observations.removeAt(0); dropped++ }
        observations.add(value)
    }

    /** Call on account switch. Does not reset the store or entitlement. */
    @Synchronized fun recordProduct(value: PurchaseProduct) {
        ensureInteropAllowed()
        require(value.productId.isNotBlank() && value.productId.length <= 256 && value.observedAt >= 0
            && value.productType in listOf("unknown", "consumable", "nonConsumable", "subscription", "nonRenewingSubscription")
            && listOf(value.displayPrice, value.price, value.currencyCode).all { (it?.length ?: 0) <= 256 })
        products.removeAll { it.productId == value.productId }
        if (products.size == 256) products.removeAt(0)
        products.add(value)
    }
    @Synchronized fun clear() { observations.clear(); products.clear(); dropped = 0; referenceSalt = UUID.randomUUID().toString() }

    @JvmOverloads @Synchronized fun execute(toolId: String, arguments: JSONObject = JSONObject(), now: Long = System.currentTimeMillis()): JSONObject {
        ensureInteropAllowed()
        require(toolId in toolIds)
        val allowed = if (toolId == "purchases.validate") listOf("productId", "transactionRef", "expectedEntitled", "requireVerified", "maxAgeMilliseconds", "requireBackendVerification", "expectedDeliveryCount")
            else if (toolId == "purchases.get_events") listOf("after") else listOf("productId", "transactionRef")
        require(arguments.keys().asSequence().all { it in allowed })
        val product = if (arguments.has("productId")) arguments.get("productId") as? String ?: error("Invalid productId") else null
        require(product == null || (product.isNotBlank() && product.length <= 256))
        val reference = if (arguments.has("transactionRef")) arguments.get("transactionRef") as? String ?: error("Invalid transactionRef") else null
        require(reference == null || (reference.length == 64 && reference.all { it in "0123456789abcdef" }))
        val rows = observations.filter { (product == null || it.productId == product) && (reference == null || it.transactionRef == reference) }
        val result = JSONObject().put("schema", "ansight.purchases.v1").put("capturedAt", now)
            .put("droppedEvents", dropped).put("coverage", "observedOnly")
            .put("observations", JSONArray(rows.map { it.toJson() }))
            .put("products", JSONArray(products.filter { product == null || it.productId == product }.map { it.toJson() }))
        if (toolId == "purchases.get_events") {
            val after = integer(arguments, "after", 0)
            require(after >= 0 && after <= dropped + observations.size)
            result.put("observations", JSONArray(observations.drop(maxOf(0, after - dropped).toInt()).map { it.toJson() }))
                .put("nextCursor", dropped + observations.size).put("cursorGap", after < dropped)
        }
        if (toolId == "purchases.validate") {
            require(product != null && arguments.get("expectedEntitled") is Boolean)
            val expected = arguments.getBoolean("expectedEntitled")
            val verify = if (arguments.has("requireVerified")) {
                require(arguments.get("requireVerified") is Boolean); arguments.getBoolean("requireVerified")
            } else true
            val maxAge = integer(arguments, "maxAgeMilliseconds", 60_000)
            require(maxAge in 1..86_400_000)
            val backend = if (arguments.has("requireBackendVerification")) {
                require(arguments.get("requireBackendVerification") is Boolean); arguments.getBoolean("requireBackendVerification")
            } else false
            val delivery = if (arguments.has("expectedDeliveryCount")) integer(arguments, "expectedDeliveryCount", 0) else null
            require((delivery ?: 0) >= 0)
            val statuses = mutableListOf<String>()
            val checks = JSONArray()
            for (source in if (backend) listOf("store", "app", "backend") else listOf("store", "app")) {
                val observation = rows.asReversed().filter { it.source == source }.maxByOrNull { it.observedAt }
                var status = "inconclusive"; var reason = "missing_evidence"
                if (observation != null) {
                    val age = now - observation.observedAt
                    if (age < 0 || age > maxAge) reason = "stale_evidence"
                    else if (observation.entitled == null && !(source == "store" && observation.productType == "consumable")) reason = "unknown_entitlement"
                    else {
                        val effectiveExpiry = if (observation.state == "gracePeriod") observation.gracePeriodExpiresAt else observation.expiresAt
                        val active = (observation.entitled ?: (observation.state == "purchased")) && (source != "store" || ((effectiveExpiry == null || effectiveExpiry > now) && observation.state !in listOf("revoked", "expired", "pending", "cancelled", "failed", "billingRetry")))
                        status = if (active == expected) "pass" else "fail"
                        reason = if (active == expected) "entitlement_matches" else "entitlement_mismatch"
                        if (source == "store" && observation.state == "gracePeriod" && effectiveExpiry == null && status == "pass" && expected) { status = "inconclusive"; reason = "missing_grace_period_expiry" }
                        if (expected && source == "store" && observation.state !in listOf("purchased", "gracePeriod")) { status = "fail"; reason = "store_not_purchased" }
                        if (status != "fail" && ((expected && source == "store" && verify && !backend) || (source == "backend" && backend)) && observation.verification != "verified") {
                            status = if (observation.verification == "unverified") "fail" else "inconclusive"
                            reason = "verification_" + observation.verification
                        }
                        if (source == "app" && delivery != null && status != "fail" && observation.deliveryCount?.toLong() != delivery) {
                            status = if (observation.deliveryCount == null) "inconclusive" else "fail"
                            reason = if (observation.deliveryCount == null) "missing_delivery_count" else "delivery_count_mismatch"
                        }
                    }
                }
                if (observation != null && now >= observation.observedAt && now - observation.observedAt <= maxAge
                    && observation.verification == "unverified"
                    && ((expected && source == "store" && verify && !backend) || (source == "backend" && backend))) {
                    status = "fail"; reason = "verification_unverified"
                }
                statuses.add(status)
                checks.put(JSONObject().put("source", source).put("status", status).put("reason", reason))
            }
            result.put("status", if ("fail" in statuses) "fail" else if ("inconclusive" in statuses) "inconclusive" else "pass").put("checks", checks)
        }
        return result
    }

    private fun integer(args: JSONObject, key: String, fallback: Long): Long {
        if (!args.has(key)) return fallback
        val value = args.get(key)
        require(value is Int || value is Long)
        return (value as Number).toLong()
    }

    fun tools(): List<AndroidTool> = toolIds.map { id ->
        val properties = if (id == "purchases.validate") mapOf("transactionRef" to ToolSchema.string(), "productId" to ToolSchema.string(), "expectedEntitled" to ToolSchema.bool(), "requireVerified" to ToolSchema.bool(), "requireBackendVerification" to ToolSchema.bool(), "expectedDeliveryCount" to ToolSchema.integer(), "maxAgeMilliseconds" to ToolSchema.integer())
            else if (id == "purchases.get_events") mapOf("after" to ToolSchema.integer()) else mapOf("transactionRef" to ToolSchema.string(), "productId" to ToolSchema.string())
        FunctionJsonAndroidTool(ToolDefinition(id, id, "Inspect passive purchase evidence. Missing evidence is inconclusive; never grants or finishes purchases.", "purchases", ToolPolicy.Read, "storekit billing entitlement validation",
            ToolSchema.obj(properties = properties, required = if (id == "purchases.validate") listOf("productId", "expectedEntitled") else emptyList()), PurchaseSchemas.result), availabilityHandler = {
                if (isInteropAllowed) ToolAvailability.Available else ToolAvailability.unavailable(
                    PurchaseEnvironmentException.ERROR_CODE, "Purchase interop is restricted to simulators and emulators.", requiredState = "simulator")
            }) { args, _ ->
            try { AndroidToolResult.success(execute(id, args)) }
            catch (error: PurchaseEnvironmentException) { AndroidToolResult.failure(error.message!!, PurchaseEnvironmentException.ERROR_CODE) }
            catch (_: Exception) { AndroidToolResult.failure("Invalid purchase arguments.", "purchases_invalid_arguments") }
        }
    }

    /** Local app bridge, never exposed as a remote mutation tool. */
    fun command(json: String): String {
        ensureInteropAllowed()
        val request = JSONObject(json)
        when (val action = request.getString("action")) {
            "reference" -> return JSONObject().put("transactionRef", createTransactionReference(request.getString("identifier"))).toString()
            "record" -> record(PurchaseObservation.fromJson(request.getJSONObject("observation")))
            "recordProduct" -> recordProduct(PurchaseProduct.fromJson(request.getJSONObject("product")))
            "clear" -> clear()
            "register" -> tools().forEach { AnsightRuntime.registerTool(it, replaceExisting = true) }
            else -> return execute(action, request.optJSONObject("arguments") ?: JSONObject()).toString()
        }
        return "{\"success\":true}"
    }

    companion object {
        @JvmSynthetic internal fun forTesting(environmentAllowed: () -> Boolean) = PurchaseDiagnostics(environmentAllowed)
        @JvmField val shared = PurchaseDiagnostics()
        val toolIds = listOf("purchases.query_products", "purchases.get_state", "purchases.query_transactions", "purchases.get_entitlements", "purchases.get_events", "purchases.validate")
    }
}

@JvmOverloads
fun AnsightOptionsBuilder.withPurchaseTools(diagnostics: PurchaseDiagnostics = PurchaseDiagnostics.shared): AnsightOptionsBuilder = addTools(diagnostics.tools())
