package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject
import java.security.KeyFactory
import java.security.MessageDigest
import java.security.PublicKey
import java.security.Signature
import java.security.spec.X509EncodedKeySpec
import java.security.interfaces.ECPublicKey
import java.text.SimpleDateFormat
import java.util.Locale
import java.util.TimeZone
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

internal object PairingV2Constants {
    const val Version = 2
    const val Transport = "wss"
    const val SignatureAlgorithm = "ES256-P1363"
    const val EnrollmentProofAlgorithm = "HMAC-SHA256"
    const val ConnectInitType = "CONNECT_INIT_V2"
    const val ConnectOfferType = "CONNECT_OFFER_V2"
    const val AuthChallengeType = "AUTH_CHALLENGE_V2"
    const val AuthEnrollType = "AUTH_ENROLL_V2"
    const val AuthProveType = "AUTH_PROVE_V2"
    const val AuthOkType = "AUTH_OK_V2"
    const val AuthErrorType = "AUTH_ERROR_V2"
    const val RememberedProfileSchema = "ansight.remembered-pairing-profile.v2"
    const val MaximumConfigLifetimeMillis = 24L * 60L * 60L * 1_000L
    const val MaximumGrantLifetimeMillis = 90L * 24L * 60L * 60L * 1_000L
    const val MaximumOfferLifetimeMillis = 60L * 1_000L
    const val MaximumChallengeLifetimeMillis = 60L * 1_000L
    const val MaximumClockSkewMillis = 5L * 60L * 1_000L
}

internal object PairingV2Time {
    private val timestamp = Regex("^(\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2})(?:\\.(\\d+))?(Z|[+-]\\d{2}:\\d{2})$")

    fun parseEpochMillis(value: String, field: String): Long {
        val match = timestamp.matchEntire(value)
            ?: throw IllegalArgumentException("Protocol v2 $field is not a valid RFC 3339 timestamp.")
        val offset = match.groupValues[3]
        if (offset != "Z") {
            require(offset.substring(1, 3).toInt() <= 23 && offset.substring(4, 6).toInt() <= 59) {
                "Protocol v2 $field has an invalid UTC offset."
            }
        }
        val parser = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).apply {
            isLenient = false
            timeZone = if (offset == "Z") TimeZone.getTimeZone("UTC") else TimeZone.getTimeZone("GMT$offset")
        }
        val seconds = parser.parse(match.groupValues[1])?.time
            ?: throw IllegalArgumentException("Protocol v2 $field is not a valid timestamp.")
        val fraction = match.groupValues[2]
        val millis = fraction.take(3).padEnd(3, '0').ifEmpty { "0" }.toLong()
        require(seconds <= Long.MAX_VALUE - millis) { "Protocol v2 $field is outside the supported timestamp range." }
        return seconds + millis
    }
}

internal object PairingV2Scopes {
    private val ordered = listOf("Read", "Write", "Delete")

    fun normalize(values: Collection<String>): List<String> {
        val requested = values.map { it.trim() }.toSet()
        require(requested.all(ordered::contains)) { "Protocol v2 scopes contain an unsupported value." }
        return ordered.filter(requested::contains)
    }
}

internal object PairingV2Authorization {
    fun allows(grant: PairingGrantV2, definition: ToolDefinition): Boolean {
        if (definition.scope.name !in grant.allowedScopes) return false
        return definition.security.level != ToolSecurityLevel.Critical || grant.allowCritical
    }
}

internal object PairingV2DowngradePolicy {
    fun claimsV2(payload: String): Boolean {
        if (payload.trim().startsWith("${PairingConfigCodeGenerator.SecureFormatPrefix}:")) return true
        if (runCatching { PairingConfigDocumentService.parseDocument(payload).config.schema == PairingConfig.SecureSchemaName }.getOrDefault(false)) {
            return true
        }
        return runCatching {
            val root = JSONObject(payload)
            root.optionalString("schema") in setOf(
                PairingConfig.SecureSchemaName,
                PairingConfigDocumentService.SecureConfigDocumentSchemaName,
                PairingRememberedProfileV2.SchemaName,
            ) || root.optJSONObject("config")?.optionalString("schema") == PairingConfig.SecureSchemaName
        }.getOrDefault(false)
    }
}

internal object PairingV2Crypto {
    fun decodeBase64Url(value: String, expectedBytes: Int? = null): ByteArray {
        require(value.isNotEmpty() && value.all { it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' || it == '-' || it == '_' }) {
            "Protocol v2 Base64URL values must be unpadded."
        }
        val decoded = runCatching { OkioCompat.decodeBase64(value) }
            .getOrElse { throw IllegalArgumentException("Protocol v2 Base64URL value is invalid.", it) }
        if (expectedBytes != null) {
            require(decoded.size == expectedBytes) { "Protocol v2 value must decode to $expectedBytes bytes." }
        }
        return decoded
    }

    fun encodeBase64Url(value: ByteArray): String = OkioCompat.encodeBase64UrlWithoutPadding(value)

    fun decodePaddedBase64(value: String, expectedBytes: Int? = null): ByteArray {
        require(value.length % 4 == 0) { "Protocol v2 Base64 values must use standard padding." }
        require(value.matches(Regex("[A-Za-z0-9+/]*={0,2}"))) { "Protocol v2 Base64 value uses an invalid alphabet." }
        val decoded = runCatching { OkioCompat.decodeBase64(value) }
            .getOrElse { throw IllegalArgumentException("Protocol v2 Base64 value is invalid.", it) }
        if (expectedBytes != null) require(decoded.size == expectedBytes) { "Protocol v2 value has an invalid decoded length." }
        return decoded
    }

    fun sha256(value: ByteArray): ByteArray = MessageDigest.getInstance("SHA-256").digest(value)

    fun sha256Base64Url(value: ByteArray): String = encodeBase64Url(sha256(value))

    fun fixedTimeEquals(left: ByteArray, right: ByteArray): Boolean = MessageDigest.isEqual(left, right)

    fun publicKey(encoded: String): PublicKey {
        val bytes = decodePaddedBase64(encoded)
        val key = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(bytes))
        require(key is ECPublicKey && key.params.curve.field.fieldSize == 256) { "Protocol v2 public key must use P-256." }
        return key
    }

    fun verifyP1363(publicKey: PublicKey, content: String, signatureBase64: String): Boolean {
        return runCatching {
            val signatureBytes = decodePaddedBase64(signatureBase64, 64)
            val verifier = Signature.getInstance("SHA256withECDSA")
            verifier.initVerify(publicKey)
            verifier.update(content.toByteArray(Charsets.UTF_8))
            verifier.verify(p1363ToDer(signatureBytes))
        }.getOrDefault(false)
    }

    fun hmacSha256(secret: ByteArray, content: String): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(secret, "HmacSHA256"))
        return mac.doFinal(content.toByteArray(Charsets.UTF_8))
    }

    fun derToP1363(der: ByteArray): ByteArray {
        require(der.size >= 8 && der[0] == 0x30.toByte()) { "ECDSA signature is not DER encoded." }
        var offset = 2
        if (der[1].toInt() and 0x80 != 0) {
            val lengthBytes = der[1].toInt() and 0x7f
            require(lengthBytes in 1..2)
            offset = 2 + lengthBytes
        }
        require(der[offset++] == 0x02.toByte())
        val rLength = der[offset++].toInt() and 0xff
        val r = der.copyOfRange(offset, offset + rLength)
        offset += rLength
        require(der[offset++] == 0x02.toByte())
        val sLength = der[offset++].toInt() and 0xff
        val s = der.copyOfRange(offset, offset + sLength)
        return unsignedFixed(r, 32) + unsignedFixed(s, 32)
    }

    private fun p1363ToDer(signature: ByteArray): ByteArray {
        val r = signature.copyOfRange(0, 32).toDerInteger()
        val s = signature.copyOfRange(32, 64).toDerInteger()
        val bodyLength = 2 + r.size + 2 + s.size
        return byteArrayOf(0x30, bodyLength.toByte(), 0x02, r.size.toByte()) +
            r + byteArrayOf(0x02, s.size.toByte()) + s
    }

    private fun ByteArray.toDerInteger(): ByteArray {
        var offset = 0
        while (offset < size - 1 && this[offset] == 0.toByte()) offset++
        val value = copyOfRange(offset, size)
        return if (value.first().toInt() and 0x80 != 0) byteArrayOf(0) + value else value
    }

    private fun unsignedFixed(value: ByteArray, length: Int): ByteArray {
        var offset = 0
        while (offset < value.size - 1 && value[offset] == 0.toByte()) offset++
        val stripped = value.copyOfRange(offset, value.size)
        require(stripped.size <= length)
        return ByteArray(length - stripped.size) + stripped
    }
}

internal object PairingV2ConfigValidator {
    fun validate(document: ParsedPairingDocument, expectedAppId: String?, nowEpochMillis: Long = System.currentTimeMillis()) {
        val config = document.config
        require(config.schema == PairingConfig.SecureSchemaName) { "Pairing config is not protocol v2." }
        validateExpectedApp(config.appId, expectedAppId)
        validateHost(config.host, nowEpochMillis)

        val grant = document.grant
        if (grant != null) {
            validateRememberedGrant(config, grant, document.clientKeyId, nowEpochMillis)
            return
        }

        require(config.signatureAlgorithm == PairingV2Constants.SignatureAlgorithm) {
            "Protocol v2 config signature algorithm is unsupported."
        }
        PairingV2Crypto.decodePaddedBase64(config.signature, 64)
        require(PairingConfigDocumentService.verifyPairingConfigSignature(config)) {
            "Connection config signature is invalid."
        }
        val issuedAt = parseTimestamp(config.issuedAt, "issuedAt")
        val expiresAt = parseTimestamp(config.expiresAt, "expiresAt")
        require(issuedAt <= nowEpochMillis + PairingV2Constants.MaximumClockSkewMillis) {
            "Protocol v2 config issuedAt is too far in the future."
        }
        require(expiresAt > nowEpochMillis - PairingV2Constants.MaximumClockSkewMillis) { "Connection config expired at ${config.expiresAt}." }
        require(expiresAt > issuedAt && expiresAt - issuedAt <= PairingV2Constants.MaximumConfigLifetimeMillis) {
            "Protocol v2 config lifetime exceeds the supported maximum."
        }
        require(config.minProtocolVersion == PairingV2Constants.Version) { "Protocol v2 config requires an unsupported protocol version." }
        require(config.allowedTransports.isNotEmpty() && config.allowedTransports.all { it == PairingV2Constants.Transport }) {
            "Protocol v2 config allows an insecure or unsupported transport."
        }
        val enrollment = requireNotNull(config.enrollment) { "Protocol v2 enrollment is missing." }
        require(enrollment.maxUses == 1) { "Protocol v2 enrollment maxUses must be 1." }
        PairingV2Crypto.decodeBase64Url(enrollment.ticketId)
        PairingV2Crypto.decodeBase64Url(enrollment.secret, 32)
        val enrollmentExpiresAt = parseTimestamp(enrollment.expiresAt, "enrollment.expiresAt")
        val grantExpiresAt = parseTimestamp(enrollment.grantExpiresAt, "enrollment.grantExpiresAt")
        require(enrollmentExpiresAt > nowEpochMillis - PairingV2Constants.MaximumClockSkewMillis && enrollmentExpiresAt <= expiresAt) {
            "Protocol v2 enrollment has an invalid expiry."
        }
        require(
            grantExpiresAt > nowEpochMillis - PairingV2Constants.MaximumClockSkewMillis &&
                grantExpiresAt - nowEpochMillis <= PairingV2Constants.MaximumGrantLifetimeMillis + PairingV2Constants.MaximumClockSkewMillis
        ) { "Protocol v2 grant expiry is invalid." }
        require(PairingV2Scopes.normalize(enrollment.maxScopes) == enrollment.maxScopes) {
            "Protocol v2 enrollment scopes are not in canonical order."
        }
    }

    fun validateHost(host: PairingHost, nowEpochMillis: Long) {
        val hostId = requireNotNull(host.hostId).trim()
        require(hostId.isNotEmpty()) { "Protocol v2 hostId is required." }
        require(!host.hostName.isNullOrBlank()) { "Protocol v2 hostName is required." }
        require(host.discoveryPort in 1..65_535) { "Protocol v2 discovery port is invalid." }
        val hostKey = PairingV2Crypto.publicKey(host.hostPubKey)
        val computedFingerprint = PairingV2Crypto.sha256(hostKey.encoded)
        val configuredFingerprint = PairingV2Crypto.decodeBase64Url(host.hostPubKeyFingerprint, 32)
        require(PairingV2Crypto.fixedTimeEquals(computedFingerprint, configuredFingerprint)) {
            "Protocol v2 host public-key fingerprint is invalid."
        }
        require(PairingV2Crypto.fixedTimeEquals(computedFingerprint, PairingV2Crypto.decodeBase64Url(hostId, 32))) {
            "Protocol v2 hostId does not match the host public key."
        }
        require(host.tlsPins.isNotEmpty()) { "Protocol v2 config does not contain a TLS pin." }
        host.tlsPins.forEach { pin ->
            PairingV2Crypto.decodeBase64Url(pin.tlsSpkiSha256, 32)
            val notBefore = parseTimestamp(pin.notBefore, "tls pin notBefore")
            val notAfter = parseTimestamp(pin.notAfter, "tls pin notAfter")
            require(notAfter > notBefore) { "Protocol v2 TLS pin validity window is invalid." }
        }
        require(host.tlsPins.any { pin -> isPinCurrent(pin, nowEpochMillis) }) { "Protocol v2 config has no currently valid TLS pin." }
    }

    fun isPinCurrent(pin: PairingTlsPinV2, nowEpochMillis: Long): Boolean {
        val notBefore = parseTimestamp(pin.notBefore, "tls pin notBefore")
        val notAfter = parseTimestamp(pin.notAfter, "tls pin notAfter")
        return nowEpochMillis >= notBefore && nowEpochMillis < notAfter
    }

    fun parseTimestamp(value: String, field: String): Long = PairingV2Time.parseEpochMillis(value, field)

    private fun validateExpectedApp(appId: String, expectedAppId: String?) {
        val normalizedExpected = expectedAppId?.trim().orEmpty()
        if (normalizedExpected.isNotEmpty()) {
            require(appId.trim() == normalizedExpected) {
                "Pairing config appId '${appId.trim()}' does not match expected app id '$normalizedExpected'."
            }
        }
    }

    private fun validateRememberedGrant(config: PairingConfig, grant: PairingGrantV2, clientKeyId: String?, nowEpochMillis: Long) {
        val normalizedClientKeyId = requireNotNull(clientKeyId).trim()
        require(normalizedClientKeyId.isNotEmpty()) { "Remembered protocol v2 profile has no client key reference." }
        PairingV2GrantVerifier.verify(grant, config, normalizedClientKeyId, nowEpochMillis)
    }
}

data class PairingGrantV2(
    val grantId: String,
    val hostId: String,
    val configId: String,
    val appId: String,
    val clientKeyId: String,
    val allowedScopes: List<String>,
    val allowCritical: Boolean,
    val issuedAt: String,
    val expiresAt: String,
    val signatureAlgorithm: String,
    val signature: String,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("grantId", grantId)
        .put("hostId", hostId)
        .put("configId", configId)
        .put("appId", appId)
        .put("clientKeyId", clientKeyId)
        .put("allowedScopes", JSONArray(PairingV2Scopes.normalize(allowedScopes)))
        .put("allowCritical", allowCritical)
        .put("issuedAt", issuedAt)
        .put("expiresAt", expiresAt)
        .put("signatureAlgorithm", signatureAlgorithm)
        .put("signature", signature)

    companion object {
        fun fromJson(json: JSONObject): PairingGrantV2 = PairingGrantV2(
            grantId = json.requiredString("grantId"),
            hostId = json.requiredString("hostId"),
            configId = json.requiredString("configId"),
            appId = json.requiredString("appId"),
            clientKeyId = json.requiredString("clientKeyId"),
            allowedScopes = json.stringList("allowedScopes"),
            allowCritical = json.optBoolean("allowCritical", false),
            issuedAt = json.requiredString("issuedAt"),
            expiresAt = json.requiredString("expiresAt"),
            signatureAlgorithm = json.requiredString("signatureAlgorithm"),
            signature = json.requiredString("signature"),
        )
    }
}

internal object PairingV2GrantVerifier {
    fun verify(grant: PairingGrantV2, config: PairingConfig, clientKeyId: String, nowEpochMillis: Long = System.currentTimeMillis()) {
        require(grant.signatureAlgorithm == PairingV2Constants.SignatureAlgorithm) { "Protocol v2 grant signature algorithm is unsupported." }
        require(grant.hostId == config.host.hostId && grant.configId == config.configId && grant.appId == config.appId) {
            "Protocol v2 grant is not bound to this host, config, and app."
        }
        require(grant.clientKeyId == clientKeyId) { "Protocol v2 grant is not bound to this client key." }
        PairingV2Crypto.decodeBase64Url(grant.grantId)
        PairingV2Crypto.decodeBase64Url(grant.clientKeyId, 32)
        require(PairingV2Scopes.normalize(grant.allowedScopes) == grant.allowedScopes) { "Protocol v2 grant scopes are not in canonical order." }
        val issuedAt = PairingV2ConfigValidator.parseTimestamp(grant.issuedAt, "grant issuedAt")
        val expiresAt = PairingV2ConfigValidator.parseTimestamp(grant.expiresAt, "grant expiresAt")
        require(
            issuedAt <= nowEpochMillis + PairingV2Constants.MaximumClockSkewMillis &&
                expiresAt > nowEpochMillis - PairingV2Constants.MaximumClockSkewMillis &&
                expiresAt > issuedAt &&
                expiresAt - issuedAt <= PairingV2Constants.MaximumGrantLifetimeMillis
        ) {
            "Protocol v2 grant is expired or not yet valid."
        }
        val valid = PairingV2Crypto.verifyP1363(
            PairingV2Crypto.publicKey(config.host.hostPubKey),
            PairingV2CanonicalJson.grant(grant),
            grant.signature,
        )
        require(valid) { "Protocol v2 grant signature is invalid." }
    }
}

internal object PairingV2CanonicalJson {
    fun grant(grant: PairingGrantV2): String = listOf(
        PairingCanonicalJson.stringField("grantId", grant.grantId),
        PairingCanonicalJson.stringField("hostId", grant.hostId),
        PairingCanonicalJson.stringField("configId", grant.configId),
        PairingCanonicalJson.stringField("appId", grant.appId),
        PairingCanonicalJson.stringField("clientKeyId", grant.clientKeyId),
        "\"allowedScopes\":${PairingCanonicalJson.serializeScopes(grant.allowedScopes)}",
        "\"allowCritical\":${grant.allowCritical}",
        PairingCanonicalJson.stringField("issuedAt", grant.issuedAt),
        PairingCanonicalJson.stringField("expiresAt", grant.expiresAt),
        PairingCanonicalJson.stringField("signatureAlgorithm", grant.signatureAlgorithm),
    ).joinToString(prefix = "{", postfix = "}", separator = ",")
}

internal data class PairingRememberedProfileV2(
    val configId: String,
    val appId: String,
    val appName: String,
    val host: PairingHost,
    val clientKeyId: String,
    val grant: PairingGrantV2,
    val discoveryHint: PairingDiscoveryHint?,
) {
    fun toParsedDocument(): ParsedPairingDocument = ParsedPairingDocument(
        config = PairingConfig(
            schema = PairingConfig.SecureSchemaName,
            configId = configId,
            appId = appId,
            appName = appName,
            issuedAt = grant.issuedAt,
            expiresAt = grant.expiresAt,
            oneTimeToken = null,
            host = host,
            challenge = null,
            signature = "remembered-grant",
            minProtocolVersion = 2,
            allowedTransports = listOf("wss"),
            enrollment = null,
            signatureAlgorithm = PairingV2Constants.SignatureAlgorithm,
        ),
        discoveryHint = discoveryHint,
        grant = grant,
        clientKeyId = clientKeyId,
    )

    fun toJson(): String {
        val hostJson = JSONObject()
            .put("hostId", host.hostId)
            .put("hostName", host.hostName)
            .put("discoveryPort", host.discoveryPort)
            .put("hostPubKey", host.hostPubKey)
            .put("hostPubKeyFingerprint", host.hostPubKeyFingerprint)
            .put("tlsPins", JSONArray(host.tlsPins.map { pin ->
                JSONObject()
                    .put("tlsSpkiSha256", pin.tlsSpkiSha256)
                    .put("notBefore", pin.notBefore)
                    .put("notAfter", pin.notAfter)
            }))
        return JSONObject()
            .put("schema", SchemaName)
            .put("configId", configId)
            .put("appId", appId)
            .put("appName", appName)
            .put("host", hostJson)
            .put("clientKeyId", clientKeyId)
            .put("grant", grant.toJson())
            .putIfNotNull("discovery", discoveryHint?.let(::discoveryToJson))
            .toString()
    }

    companion object {
        const val SchemaName = PairingV2Constants.RememberedProfileSchema

        fun create(document: ParsedPairingDocument, clientKeyId: String, grant: PairingGrantV2): PairingRememberedProfileV2 {
            return PairingRememberedProfileV2(
                configId = document.config.configId,
                appId = document.config.appId,
                appName = document.config.appName,
                host = document.config.host,
                clientKeyId = clientKeyId,
                grant = grant,
                discoveryHint = document.discoveryHint,
            )
        }

        fun fromJson(json: JSONObject): PairingRememberedProfileV2 = PairingRememberedProfileV2(
            configId = json.requiredString("configId"),
            appId = json.requiredString("appId"),
            appName = json.requiredString("appName"),
            host = PairingHost.fromJson(json.getJSONObject("host")),
            clientKeyId = json.requiredString("clientKeyId"),
            grant = PairingGrantV2.fromJson(json.getJSONObject("grant")),
            discoveryHint = PairingDiscoveryHint.fromJson(json.optJSONObject("discovery")),
        )

        private fun discoveryToJson(hint: PairingDiscoveryHint): JSONObject = JSONObject()
            .put("schema", hint.schema)
            .putIfNotNull("source", hint.source)
            .put("hostAddresses", JSONArray(hint.hostAddresses))
            .putIfNotNull("discoveryPort", hint.discoveryPort)
            .putIfNotNull("hostName", hint.hostName)
            .putIfNotNull("wifiName", hint.wifiName)
            .putIfNotNull("capturedAt", hint.capturedAt)
    }
}
