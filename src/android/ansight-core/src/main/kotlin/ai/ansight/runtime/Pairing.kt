package ai.ansight.runtime

import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.URLEncoder
import java.security.KeyFactory
import java.security.Signature
import java.security.spec.X509EncodedKeySpec
import java.time.Instant
import java.time.OffsetDateTime
import java.util.Locale
import java.util.zip.GZIPInputStream

object PairingProtocolDefaults {
    const val DiscoveryPort = 45_123
    const val WebSocketPort = 45_124
    const val WebSocketPath = "/ws"
}

data class PairingConfig(
    val schema: String,
    val configId: String,
    val appId: String,
    val appName: String,
    val issuedAt: String,
    val expiresAt: String,
    val oneTimeToken: String?,
    val host: PairingHost,
    val challenge: PairingChallenge?,
    val signature: String,
    val minProtocolVersion: Int,
    val allowedTransports: List<String>,
    val enrollment: PairingEnrollmentV2?,
    val signatureAlgorithm: String?,
) {
    constructor(
        schema: String,
        configId: String,
        appId: String,
        appName: String,
        issuedAt: String,
        expiresAt: String,
        oneTimeToken: String,
        host: PairingHost,
        challenge: PairingChallenge,
        signature: String,
    ) : this(
        schema,
        configId,
        appId,
        appName,
        issuedAt,
        expiresAt,
        oneTimeToken,
        host,
        challenge,
        signature,
        minProtocolVersion = 1,
        allowedTransports = emptyList(),
        enrollment = null,
        signatureAlgorithm = null,
    )

    companion object {
        const val SchemaName = "ansight.pairing-config.v1"
        const val SecureSchemaName = "ansight.pairing-config.v2"

        fun fromJson(json: JSONObject): PairingConfig {
            val schema = json.requiredString("schema")
            return PairingConfig(
                schema = schema,
                configId = json.requiredString("configId"),
                appId = json.requiredString("appId"),
                appName = json.requiredString("appName"),
                issuedAt = json.requiredString("issuedAt"),
                expiresAt = json.requiredString("expiresAt"),
                oneTimeToken = json.optionalString("oneTimeToken"),
                host = PairingHost.fromJson(json.getJSONObject("host")),
                challenge = json.optJSONObject("challenge")?.let(PairingChallenge::fromJson),
                signature = json.requiredString("signature"),
                minProtocolVersion = json.optionalInt("minProtocolVersion") ?: if (schema == SecureSchemaName) 2 else 1,
                allowedTransports = json.stringList("allowedTransports"),
                enrollment = json.optJSONObject("enrollment")?.let(PairingEnrollmentV2::fromJson),
                signatureAlgorithm = json.optionalString("signatureAlgorithm"),
            )
        }
    }
}

data class PairingHost(
    val hostId: String?,
    val hostName: String?,
    val discoveryPort: Int,
    val hostPubKey: String,
    val hostPubKeyFingerprint: String,
    val tlsPins: List<PairingTlsPinV2>,
) {
    constructor(
        hostId: String?,
        hostName: String?,
        discoveryPort: Int,
        hostPubKey: String,
        hostPubKeyFingerprint: String,
    ) : this(hostId, hostName, discoveryPort, hostPubKey, hostPubKeyFingerprint, emptyList())

    companion object {
        fun fromJson(json: JSONObject): PairingHost = PairingHost(
            hostId = json.optionalString("hostId"),
            hostName = json.optionalString("hostName"),
            discoveryPort = json.optionalInt("discoveryPort") ?: PairingProtocolDefaults.DiscoveryPort,
            hostPubKey = json.requiredString("hostPubKey"),
            hostPubKeyFingerprint = json.requiredString("hostPubKeyFingerprint"),
            tlsPins = json.objectList("tlsPins", PairingTlsPinV2::fromJson),
        )
    }
}

data class PairingTlsPinV2(
    val tlsSpkiSha256: String,
    val notBefore: String,
    val notAfter: String,
) {
    companion object {
        fun fromJson(json: JSONObject): PairingTlsPinV2 = PairingTlsPinV2(
            tlsSpkiSha256 = json.requiredString("tlsSpkiSha256"),
            notBefore = json.requiredString("notBefore"),
            notAfter = json.requiredString("notAfter"),
        )
    }
}

data class PairingEnrollmentV2(
    val ticketId: String,
    val secret: String,
    val expiresAt: String,
    val grantExpiresAt: String,
    val maxUses: Int,
    val maxScopes: List<String>,
    val allowCritical: Boolean,
) {
    companion object {
        fun fromJson(json: JSONObject): PairingEnrollmentV2 = PairingEnrollmentV2(
            ticketId = json.requiredString("ticketId"),
            secret = json.requiredString("secret"),
            expiresAt = json.requiredString("expiresAt"),
            grantExpiresAt = json.requiredString("grantExpiresAt"),
            maxUses = json.optInt("maxUses", 0),
            maxScopes = json.stringList("maxScopes"),
            allowCritical = json.optBoolean("allowCritical", false),
        )
    }
}

data class PairingChallenge(
    val alg: String,
    val challengePubKey: String,
    val requireProofOnFirstPair: Boolean,
) {
    companion object {
        fun fromJson(json: JSONObject): PairingChallenge = PairingChallenge(
            alg = json.requiredString("alg"),
            challengePubKey = json.requiredString("challengePubKey"),
            requireProofOnFirstPair = json.optionalBoolean("requireProofOnFirstPair") ?: false,
        )
    }
}

data class PairingDiscoveryHint(
    val schema: String = "ansight.discovery-hint.v1",
    val source: String? = null,
    val hostAddresses: List<String> = emptyList(),
    val discoveryPort: Int? = null,
    val hostName: String? = null,
    val wifiName: String? = null,
    val capturedAt: String? = null,
) {
    val hostAddress: String?
        get() = hostAddressCandidates.firstOrNull()

    val hostAddressCandidates: List<String>
        get() = hostAddresses.mapNotNull { address ->
            address.trim().ifBlank { null }
        }.distinct()

    companion object {
        fun fromJson(json: JSONObject?): PairingDiscoveryHint? {
            if (json == null) {
                return null
            }

            val addresses = mutableListOf<String>()
            val rawAddresses = json.optJSONArray("hostAddresses")
            if (rawAddresses != null) {
                for (index in 0 until rawAddresses.length()) {
                    rawAddresses.optString(index).trim().ifBlank { null }?.let { address -> addresses.add(address) }
                }
            }

            json.optionalString("hostAddress")?.let { address -> addresses.add(address) }

            return PairingDiscoveryHint(
                schema = json.optionalString("schema") ?: "ansight.discovery-hint.v1",
                source = json.optionalString("source"),
                hostAddresses = addresses.distinct(),
                discoveryPort = json.optionalInt("discoveryPort"),
                hostName = json.optionalString("hostName"),
                wifiName = json.optionalString("wifiName"),
                capturedAt = json.optionalString("capturedAt"),
            )
        }
    }
}

data class ParsedPairingDocument(
    val config: PairingConfig,
    val discoveryHint: PairingDiscoveryHint? = null,
    val grant: PairingGrantV2? = null,
    val clientKeyId: String? = null,
)

object PairingConfigDocumentService {
    const val ConfigDocumentSchemaName = "ansight.pairing-config-document.v1"
    const val SecureConfigDocumentSchemaName = "ansight.pairing-config-document.v2"
    const val LegacySchemaName = "ansight.pairing-ticket.v1"

    fun parseAndValidateDocument(payload: String, expectedAppId: String? = null): ParsedPairingDocument {
        val document = parseDocument(payload)
        validateDocument(document, expectedAppId)
        return document
    }

    fun parseDocument(payload: String): ParsedPairingDocument {
        val trimmed = payload.trim()
        require(trimmed.isNotEmpty()) { "Paste or load a pairing config." }

        PairingConfigCodeGenerator.tryParse(trimmed)?.let { document -> return document }

        val root = try {
            JSONObject(trimmed)
        } catch (ex: Exception) {
            throw IllegalArgumentException("Failed to parse pairing config: ${ex.message}", ex)
        }

        val schema = root.optionalString("schema")
        if (schema == "ansight.pairing-bootstrap.v1") {
            throw IllegalArgumentException("Legacy bootstrap pairing payloads are no longer supported. Export a fresh pairing config from Ansight Studio.")
        }

        if (schema == PairingConfig.SchemaName || schema == PairingConfig.SecureSchemaName) {
            return ParsedPairingDocument(PairingConfig.fromJson(root))
        }

        if (schema == PairingRememberedProfileV2.SchemaName) {
            return PairingRememberedProfileV2.fromJson(root).toParsedDocument()
        }

        if (schema == ConfigDocumentSchemaName || schema == SecureConfigDocumentSchemaName || schema == LegacySchemaName) {
            val configObject = root.optJSONObject("config")
                ?: throw IllegalArgumentException("Pairing config document did not contain a pairing config.")
            return ParsedPairingDocument(
                config = PairingConfig.fromJson(configObject),
                discoveryHint = PairingDiscoveryHint.fromJson(root.optJSONObject("discovery")),
            )
        }

        val resolvedSchema = schema?.trim().orEmpty()
        if (resolvedSchema.isEmpty()) {
            throw IllegalArgumentException("Pairing payloads must be pairing configs.")
        }

        throw IllegalArgumentException("Unsupported pairing payload schema '$resolvedSchema'. Export a fresh pairing config from Ansight Studio.")
    }

    fun validateDocument(document: ParsedPairingDocument, expectedAppId: String? = null) {
        val config = document.config
        require(config.schema == PairingConfig.SchemaName || config.schema == PairingConfig.SecureSchemaName) {
            "Unsupported pairing config schema '${config.schema}'."
        }
        if (config.schema == PairingConfig.SecureSchemaName) {
            PairingV2ConfigValidator.validate(document, expectedAppId)
            return
        }
        require(verifyPairingConfigSignature(config)) {
            "Connection config signature is invalid."
        }

        val expiresAt = parseConfigInstant(config.expiresAt)
        require(!Instant.now().isAfter(expiresAt)) {
            "Connection config expired at ${config.expiresAt}."
        }

        val normalizedExpected = expectedAppId?.trim().orEmpty()
        if (normalizedExpected.isNotEmpty()) {
            require(config.appId.trim() == normalizedExpected) {
                "Pairing config appId '${config.appId.trim()}' does not match expected app id '$normalizedExpected'."
            }
        }
    }

    internal fun verifyPairingConfigSignature(config: PairingConfig): Boolean {
        return try {
            val publicKeyBytes = OkioCompat.decodeBase64(config.host.hostPubKey)
            val signatureBytes = OkioCompat.decodeBase64(config.signature)
            val publicKey = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(publicKeyBytes))
            val derSignature = ensureDerSignature(signatureBytes)
            val signables = if (config.schema == PairingConfig.SecureSchemaName) {
                listOf(PairingCanonicalJson.serializePairingConfigV2ForSignature(config))
            } else {
                listOf(
                    PairingCanonicalJson.serializePairingConfigForSignature(config),
                    PairingCanonicalJson.serializePairingConfigWithLegacyTrustForSignature(config),
                )
            }
            signables.any { signable ->
                val verifier = Signature.getInstance("SHA256withECDSA")
                verifier.initVerify(publicKey)
                verifier.update(signable.toByteArray(Charsets.UTF_8))
                verifier.verify(derSignature)
            }
        } catch (_: Exception) {
            false
        }
    }

    internal fun parseConfigInstant(value: String): Instant =
        runCatching { Instant.parse(value) }
            .recoverCatching { OffsetDateTime.parse(value).toInstant() }
            .getOrElse { throw IllegalArgumentException("Pairing config expiry could not be parsed.", it) }

    private fun ensureDerSignature(signature: ByteArray): ByteArray {
        if (signature.firstOrNull() == 0x30.toByte()) {
            return signature
        }

        if (signature.size != 64) {
            return signature
        }

        val r = signature.copyOfRange(0, 32).toDerInteger()
        val s = signature.copyOfRange(32, 64).toDerInteger()
        val bodyLength = 2 + r.size + 2 + s.size
        return byteArrayOf(0x30, bodyLength.toByte(), 0x02, r.size.toByte()) +
            r +
            byteArrayOf(0x02, s.size.toByte()) +
            s
    }

    private fun ByteArray.toDerInteger(): ByteArray {
        var offset = 0
        while (offset < size - 1 && this[offset] == 0.toByte()) {
            offset++
        }

        val value = copyOfRange(offset, size)
        return if (value.first().toInt() and 0x80 != 0) {
            byteArrayOf(0) + value
        } else {
            value
        }
    }
}

internal object PairingConfigCodeGenerator {
    const val FormatPrefix = "apc1"
    const val SecureFormatPrefix = "apc2"
    const val LegacyFormatPrefix = "apt1"

    fun tryParse(payload: String): ParsedPairingDocument? {
        val normalizedPayload = payload.trim()
        val formatPrefix = when {
            normalizedPayload.startsWith("$SecureFormatPrefix:") -> SecureFormatPrefix
            normalizedPayload.startsWith("$FormatPrefix:") -> FormatPrefix
            normalizedPayload.startsWith("$LegacyFormatPrefix:") -> LegacyFormatPrefix
            else -> return null
        }

        val encodedPayload = normalizedPayload.substring(formatPrefix.length + 1)
        val compressedBytes = runCatching {
            OkioCompat.decodeBase64(encodedPayload)
        }.getOrNull() ?: return null

        val json = runCatching {
            GZIPInputStream(compressedBytes.inputStream()).use { gzip ->
                String(gzip.readBytes(), Charsets.UTF_8)
            }
        }.getOrNull() ?: return null

        return runCatching {
            val root = JSONObject(json)
            val schema = root.optionalString("schema")
            if (schema != PairingConfigDocumentService.ConfigDocumentSchemaName &&
                schema != PairingConfigDocumentService.SecureConfigDocumentSchemaName &&
                schema != PairingConfigDocumentService.LegacySchemaName
            ) {
                return null
            }

            val configObject = root.optJSONObject("config") ?: return null
            ParsedPairingDocument(
                config = PairingConfig.fromJson(configObject),
                discoveryHint = PairingDiscoveryHint.fromJson(root.optJSONObject("discovery")),
            )
        }.getOrNull()
    }
}

internal object PairingCanonicalJson {
    fun serializePairingConfigForSignature(config: PairingConfig): String {
        return serializePairingConfig(config, includeLegacyTrust = false)
    }

    fun serializePairingConfigWithLegacyTrustForSignature(config: PairingConfig): String {
        return serializePairingConfig(config, includeLegacyTrust = true)
    }

    fun serializePairingConfigV2ForSignature(config: PairingConfig): String {
        val enrollment = requireNotNull(config.enrollment) { "Protocol v2 config enrollment is missing." }
        val pins = config.host.tlsPins.sortedWith(compareBy<PairingTlsPinV2> { it.notBefore }.thenBy { it.tlsSpkiSha256 })
        return listOf(
            jsonStringField("schema", config.schema, escapePlus = true),
            jsonStringField("configId", config.configId, escapePlus = true),
            jsonStringField("appId", config.appId, escapePlus = true),
            jsonStringField("appName", config.appName, escapePlus = true),
            jsonStringField("issuedAt", config.issuedAt, escapePlus = true),
            jsonStringField("expiresAt", config.expiresAt, escapePlus = true),
            "\"minProtocolVersion\":${config.minProtocolVersion}",
            "\"allowedTransports\":${serializeStringArray(config.allowedTransports)}",
            "\"host\":${serializeHostV2(config.host, pins)}",
            "\"enrollment\":${serializeEnrollmentV2(enrollment)}",
            jsonStringField("signatureAlgorithm", config.signatureAlgorithm.orEmpty(), escapePlus = true),
        ).joinToString(prefix = "{", postfix = "}", separator = ",")
    }

    private fun serializePairingConfig(config: PairingConfig, includeLegacyTrust: Boolean): String {
        val oneTimeToken = requireNotNull(config.oneTimeToken) { "Protocol v1 config token is missing." }
        val challenge = requireNotNull(config.challenge) { "Protocol v1 challenge is missing." }
        val hostJson = serializeHost(config.host.hostPubKey, config.host.hostPubKeyFingerprint)
        val fields = mutableListOf(
            jsonStringField("schema", config.schema, escapePlus = true),
            jsonStringField("configId", config.configId, escapePlus = true),
            jsonStringField("appId", config.appId, escapePlus = true),
            jsonStringField("appName", config.appName, escapePlus = true),
            jsonStringField("issuedAt", config.issuedAt, escapePlus = false),
            jsonStringField("expiresAt", config.expiresAt, escapePlus = false),
            jsonStringField("oneTimeToken", oneTimeToken, escapePlus = true),
            "\"host\":$hostJson",
            "\"challenge\":${serializeChallenge(challenge)}",
        )
        if (includeLegacyTrust) {
            fields.add("\"trust\":${serializeLegacyTrust()}")
        }

        return fields.joinToString(prefix = "{", postfix = "}", separator = ",")
    }

    private fun serializeHost(hostPubKey: String, hostPubKeyFingerprint: String): String {
        return listOf(
            jsonStringField("hostPubKey", hostPubKey, escapePlus = true),
            jsonStringField("hostPubKeyFingerprint", hostPubKeyFingerprint, escapePlus = true),
        ).joinToString(prefix = "{", postfix = "}", separator = ",")
    }

    private fun serializeChallenge(challenge: PairingChallenge): String {
        return listOf(
            jsonStringField("alg", challenge.alg, escapePlus = true),
            jsonStringField("challengePubKey", challenge.challengePubKey, escapePlus = true),
            "\"requireProofOnFirstPair\":${challenge.requireProofOnFirstPair}",
        ).joinToString(prefix = "{", postfix = "}", separator = ",")
    }

    private fun serializeHostV2(host: PairingHost, pins: List<PairingTlsPinV2>): String = listOf(
        jsonStringField("hostId", host.hostId.orEmpty(), escapePlus = true),
        jsonStringField("hostName", host.hostName.orEmpty(), escapePlus = true),
        "\"discoveryPort\":${host.discoveryPort}",
        jsonStringField("hostPubKey", host.hostPubKey, escapePlus = true),
        jsonStringField("hostPubKeyFingerprint", host.hostPubKeyFingerprint, escapePlus = true),
        "\"tlsPins\":${pins.joinToString(prefix = "[", postfix = "]", separator = ",") { pin -> serializeTlsPinV2(pin) }}",
    ).joinToString(prefix = "{", postfix = "}", separator = ",")

    private fun serializeTlsPinV2(pin: PairingTlsPinV2): String = listOf(
        jsonStringField("tlsSpkiSha256", pin.tlsSpkiSha256, escapePlus = true),
        jsonStringField("notBefore", pin.notBefore, escapePlus = true),
        jsonStringField("notAfter", pin.notAfter, escapePlus = true),
    ).joinToString(prefix = "{", postfix = "}", separator = ",")

    private fun serializeEnrollmentV2(enrollment: PairingEnrollmentV2): String = listOf(
        jsonStringField("ticketId", enrollment.ticketId, escapePlus = true),
        jsonStringField("secret", enrollment.secret, escapePlus = true),
        jsonStringField("expiresAt", enrollment.expiresAt, escapePlus = true),
        jsonStringField("grantExpiresAt", enrollment.grantExpiresAt, escapePlus = true),
        "\"maxUses\":${enrollment.maxUses}",
        "\"maxScopes\":${serializeScopes(enrollment.maxScopes)}",
        "\"allowCritical\":${enrollment.allowCritical}",
    ).joinToString(prefix = "{", postfix = "}", separator = ",")

    internal fun serializeStringArray(values: List<String>): String =
        values.joinToString(prefix = "[", postfix = "]", separator = ",") { value ->
            "\"${escapeJsonString(value, escapePlus = true)}\""
        }

    internal fun serializeScopes(values: List<String>): String = serializeStringArray(PairingV2Scopes.normalize(values))

    internal fun stringField(name: String, value: String): String = jsonStringField(name, value, escapePlus = true)

    private fun serializeLegacyTrust(): String {
        return listOf(
            jsonStringField("mode", "pinned-key+token+challenge", escapePlus = true),
            "\"requireTokenOnFirstPair\":true",
            "\"allowLanDiscovery\":false",
        ).joinToString(prefix = "{", postfix = "}", separator = ",")
    }

    private fun jsonStringField(name: String, value: String, escapePlus: Boolean): String {
        return "\"$name\":\"${escapeJsonString(value, escapePlus)}\""
    }

    private fun escapeJsonString(value: String, escapePlus: Boolean): String {
        val builder = StringBuilder(value.length)
        value.forEach { char ->
            when (char) {
                '"' -> builder.append("\\\"")
                '\\' -> builder.append("\\\\")
                '\n' -> builder.append("\\n")
                '\r' -> builder.append("\\r")
                '\t' -> builder.append("\\t")
                '+' -> if (escapePlus) builder.append("\\u002B") else builder.append('+')
                '\'' -> builder.append("\\u0027")
                '&' -> builder.append("\\u0026")
                '<' -> builder.append("\\u003C")
                '>' -> builder.append("\\u003E")
                '`' -> builder.append("\\u0060")
                else -> {
                    val code = char.code
                    if (code < 0x20 || code > 0x7e) {
                        builder.append("\\u")
                        builder.append(code.toString(16).uppercase(Locale.US).padStart(4, '0'))
                    } else {
                        builder.append(char)
                    }
                }
            }
        }
        return builder.toString()
    }
}

data class ConnectRequest(
    val configId: String,
    val oneTimeToken: String,
    val appId: String,
    val clientName: String,
    val processSessionId: String? = ProcessSessionIdentity.current,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("type", "CONNECT_REQ")
        .put("ver", 1)
        .put("configId", configId)
        .put("oneTimeToken", oneTimeToken)
        .put("appId", appId)
        .put("clientName", clientName)
        .putIfNotNull("processSessionId", processSessionId)
}

data class ConnectResponse(
    val type: String,
    val ver: Int,
    val accepted: Boolean,
    val reason: String,
    val reasonMessage: String?,
    val hostId: String,
    val hostName: String,
    val hostWifiName: String?,
    val message: String,
    val webSocketPort: Int?,
    val webSocketPath: String?,
    val webSocketToken: String?,
) {
    companion object {
        fun fromJson(json: JSONObject): ConnectResponse = ConnectResponse(
            type = json.requiredString("type"),
            ver = json.optInt("ver", 1),
            accepted = json.optBoolean("accepted", false),
            reason = json.requiredString("reason"),
            reasonMessage = json.optionalString("reasonMessage"),
            hostId = json.optionalString("hostId") ?: "",
            hostName = json.optionalString("hostName") ?: "",
            hostWifiName = json.optionalString("hostWifiName"),
            message = json.optionalString("message") ?: "",
            webSocketPort = json.optionalInt("webSocketPort"),
            webSocketPath = json.optionalString("webSocketPath"),
            webSocketToken = json.optionalString("webSocketToken"),
        )
    }
}

object PairingControlActions {
    const val SessionOpen = "session.open"
    const val SessionProperties = "session.properties"
    const val SessionComplete = "session.complete"
    const val ClientLog = "client.log"
    const val DeviceProfile = "device.profile"
    const val AppState = "app.state"
}

object ProcessSessionIdentity {
    val current: String = "android.${java.util.UUID.randomUUID().toString().replace("-", "")}"
}

internal object PairingSimulatorLocalHostAddress {
    fun resolve(): String? = runCatching {
        if (isAndroidEmulator()) {
            androidHostAddress()
        } else {
            null
        }
    }.getOrNull()

    private fun androidHostAddress(): String =
        if (Build.MANUFACTURER.orEmpty().contains("Genymotion", ignoreCase = true)) {
            "10.0.3.2"
        } else {
            "10.0.2.2"
        }

    private fun isAndroidEmulator(): Boolean {
        val fingerprint = Build.FINGERPRINT.orEmpty()
        val model = Build.MODEL.orEmpty()
        val product = Build.PRODUCT.orEmpty()
        val manufacturer = Build.MANUFACTURER.orEmpty()
        val brand = Build.BRAND.orEmpty()
        val device = Build.DEVICE.orEmpty()

        return fingerprint.contains("generic", ignoreCase = true) ||
            fingerprint.contains("emulator", ignoreCase = true) ||
            model.contains("Emulator", ignoreCase = true) ||
            model.contains("Android SDK built for", ignoreCase = true) ||
            manufacturer.contains("Genymotion", ignoreCase = true) ||
            (brand.startsWith("generic", ignoreCase = true) && device.startsWith("generic", ignoreCase = true)) ||
            product.contains("sdk", ignoreCase = true)
    }
}

internal object PairingHostAddressCandidates {
    fun resolve(
        discoveryHint: PairingDiscoveryHint?,
        hostAddressOverride: String?,
        simulatorLocalHostAddress: String?,
    ): List<String> {
        hostAddressOverride?.trim()?.ifBlank { null }?.let { return listOf(it) }

        return (listOf(simulatorLocalHostAddress) + discoveryHint?.hostAddressCandidates.orEmpty())
            .mapNotNull { address -> address?.trim()?.ifBlank { null } }
            .distinct()
    }
}

data class PairingConnectionOptions(
    val hostAddressOverride: String? = null,
    val discoveryPort: Int? = null,
    val allowInsecureV1: Boolean = false,
    val requestedScopes: List<String> = emptyList(),
    val requestCritical: Boolean = false,
)

data class PairingConnectionAttempt(
    val success: Boolean,
    val accepted: Boolean,
    val message: String,
    val hostAddress: String? = null,
    val connectResponse: ConnectResponse? = null,
    val transport: PairingLiveSessionTransport? = null,
    val failureCode: String? = null,
    val authenticationV2: PairingV2AuthenticationResult? = null,
) {
    companion object {
        fun failure(message: String, code: String? = null) = PairingConnectionAttempt(false, false, message, failureCode = code)
        fun rejected(hostAddress: String, response: ConnectResponse) = PairingConnectionAttempt(
            success = false,
            accepted = false,
            message = response.reasonMessage ?: response.message,
            hostAddress = hostAddress,
            connectResponse = response,
        )
        fun success(
            hostAddress: String,
            response: ConnectResponse,
            transport: PairingLiveSessionTransport,
            authenticationV2: PairingV2AuthenticationResult? = null,
        ) = PairingConnectionAttempt(
            success = true,
            accepted = true,
            message = "Connected to host and WebSocket session is ready.",
            hostAddress = hostAddress,
            connectResponse = response,
            transport = transport,
            authenticationV2 = authenticationV2,
        )
    }
}

class PairingSessionConnector(
    private val simulatorLocalHostAddressProvider: () -> String? = { PairingSimulatorLocalHostAddress.resolve() },
    private val pairingV2Authenticator: PairingV2Authenticator = PairingV2Authenticator(),
) {
    fun connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions = PairingConnectionOptions(),
    ): PairingConnectionAttempt {
        val isSecureV2 = document.config.schema == PairingConfig.SecureSchemaName
        if (isSecureV2 && Build.VERSION.SDK_INT < Build.VERSION_CODES.M) {
            return PairingConnectionAttempt.failure(
                "Secure protocol v2 pairing requires Android 6.0 (API 23) or newer for a non-exportable P-256 client key.",
                PairingFailureCodes.PlatformSecurityUnavailable,
            )
        }
        if (isSecureV2) {
            try {
                PairingV2ConfigValidator.validate(document, expectedAppId = null)
            } catch (ex: Exception) {
                return PairingConnectionAttempt.failure(
                    PairingRedaction.redact(ex.message ?: "Protocol v2 pairing config is invalid."),
                    PairingFailureCodes.PairingProofInvalid,
                )
            }
        }
        if (!isSecureV2 && !options.allowInsecureV1) {
            return PairingConnectionAttempt.failure(
                "Protocol v1 pairing is insecure and disabled. Enable AllowInsecureV1 only for an explicit development connection.",
                PairingFailureCodes.InsecureV1Disabled,
            )
        }
        val hostAddressCandidates = PairingHostAddressCandidates.resolve(
            document.discoveryHint,
            options.hostAddressOverride,
            resolveSimulatorLocalHostAddress(),
        )
        if (hostAddressCandidates.isEmpty()) {
            return PairingConnectionAttempt.failure(
                "A current host address is required. Import a fresh pairing config or compact pairing config code.",
                PairingFailureCodes.HostAddressRequired,
            )
        }

        val discoveryPort = options.discoveryPort
            ?: document.discoveryHint?.discoveryPort
            ?: document.config.host.discoveryPort

        if (discoveryPort !in 1..65_535) {
            return PairingConnectionAttempt.failure("Pairing discovery port must be between 1 and 65535.", PairingFailureCodes.HostAddressRequired)
        }

        var lastFailure: PairingConnectionAttempt? = null
        for (hostAddress in hostAddressCandidates) {
            if (isSecureV2) {
                val secureAttempt = connectV2(document, hostAddress, discoveryPort, options)
                if (secureAttempt.success || secureAttempt.failureCode != PairingFailureCodes.UdpBootstrapTimeout) {
                    return secureAttempt
                }
                lastFailure = secureAttempt
                continue
            }
            val connectResponse = try {
                sendConnectRequest(document.config, clientName, hostAddress, discoveryPort)
            } catch (ex: Exception) {
                lastFailure = PairingConnectionAttempt.failure("UDP connect failed for $hostAddress: ${ex.message}", PairingFailureCodes.UdpBootstrapFailed)
                continue
            }

            if (connectResponse == null) {
                lastFailure = PairingConnectionAttempt.failure(
                    "No connect response from host at $hostAddress. Check that this device is on the same Wi-Fi network as the Ansight host.",
                    PairingFailureCodes.UdpBootstrapTimeout,
                )
                continue
            }

            if (connectResponse.type != "CONNECT_RESP") {
                return PairingConnectionAttempt.failure("Host connect response had unexpected type '${connectResponse.type}'.", PairingFailureCodes.UdpBootstrapFailed)
            }

            if (!connectResponse.accepted) {
                return PairingConnectionAttempt.rejected(hostAddress, connectResponse)
            }

            val webSocketPort = connectResponse.webSocketPort
            val webSocketPath = connectResponse.webSocketPath?.trim()
            val webSocketToken = connectResponse.webSocketToken?.trim()
            if (webSocketPort == null || webSocketPath.isNullOrBlank() || webSocketToken.isNullOrBlank()) {
                return PairingConnectionAttempt.failure("Host did not provide a WebSocket handoff.", PairingFailureCodes.WebSocketHandoffUnavailable)
            }

            val url = buildWebSocketUrl(hostAddress, webSocketPort, webSocketPath, webSocketToken)
            val transport = PairingLiveSessionTransport()
            val openResult = transport.open(url)
            if (!openResult.success) {
                return PairingConnectionAttempt.failure(openResult.message, PairingFailureCodes.WebSocketEndpointUnreachable)
            }

            return PairingConnectionAttempt.success(hostAddress, connectResponse, transport)
        }

        return lastFailure ?: PairingConnectionAttempt.failure(
            "A current host address is required. Import a fresh pairing config or compact pairing config code.",
            PairingFailureCodes.HostAddressRequired,
        )
    }

    private fun resolveSimulatorLocalHostAddress(): String? =
        runCatching { simulatorLocalHostAddressProvider()?.trim()?.ifBlank { null } }.getOrNull()

    private fun sendConnectRequest(
        config: PairingConfig,
        clientName: String,
        hostAddress: String,
        discoveryPort: Int,
    ): ConnectResponse? {
        val address = InetAddress.getByName(hostAddress)
        val socket = DatagramSocket()
        socket.soTimeout = 5_000
        socket.use {
            val request = ConnectRequest(
                configId = config.configId,
                oneTimeToken = requireNotNull(config.oneTimeToken) { "Protocol v1 pairing token is missing." },
                appId = config.appId,
                clientName = clientName,
            ).toJson().toString().toByteArray(Charsets.UTF_8)

            it.send(DatagramPacket(request, request.size, address, discoveryPort))
            val buffer = ByteArray(16 * 1024)
            val packet = DatagramPacket(buffer, buffer.size)
            while (true) {
                it.receive(packet)
                if (packet.address != address) {
                    continue
                }

                val json = JSONObject(String(packet.data, packet.offset, packet.length, Charsets.UTF_8))
                return ConnectResponse.fromJson(json)
            }
        }
    }

    private fun connectV2(
        document: ParsedPairingDocument,
        hostAddress: String,
        discoveryPort: Int,
        options: PairingConnectionOptions,
    ): PairingConnectionAttempt {
        val init = ConnectInitV2Factory.create(document.config)
        val offer = try {
            sendConnectInitV2(init, document.config, hostAddress, discoveryPort)
        } catch (ex: java.net.SocketTimeoutException) {
            return PairingConnectionAttempt.failure(
                "No protocol v2 connect offer from host at $hostAddress.",
                PairingFailureCodes.UdpBootstrapTimeout,
            )
        } catch (ex: Exception) {
            return PairingConnectionAttempt.failure(
                PairingRedaction.redact("Protocol v2 UDP bootstrap failed for $hostAddress: ${ex.message}"),
                PairingFailureCodes.UdpBootstrapFailed,
            )
        }

        val url = buildWebSocketUrlV2(hostAddress, offer.webSocketPort, offer.webSocketPath)
        val transport = PairingLiveSessionTransport()
        val openResult = transport.openPinnedWss(url, offer.tlsSpkiSha256)
        if (!openResult.success) {
            transport.close(notify = false)
            return PairingConnectionAttempt.failure(
                PairingRedaction.redact(openResult.message),
                PairingFailureCodes.TlsValidationFailed,
            )
        }

        val authentication = try {
            pairingV2Authenticator.authenticate(
                transport = transport,
                document = document,
                init = init,
                offer = offer,
                requestedScopes = options.requestedScopes,
                requestCritical = options.requestCritical,
            )
        } catch (ex: Exception) {
            transport.close(notify = false)
            return PairingConnectionAttempt.failure(
                PairingRedaction.redact(ex.message ?: "Protocol v2 authentication failed."),
                PairingFailureCodes.PairingProofInvalid,
            )
        }

        val response = ConnectResponse(
            type = PairingV2Constants.ConnectOfferType,
            ver = 2,
            accepted = true,
            reason = "ok",
            reasonMessage = null,
            hostId = offer.hostId,
            hostName = document.config.host.hostName.orEmpty(),
            hostWifiName = document.discoveryHint?.wifiName,
            message = "Authenticated protocol v2 session is ready.",
            webSocketPort = offer.webSocketPort,
            webSocketPath = offer.webSocketPath,
            webSocketToken = null,
        )
        return PairingConnectionAttempt.success(hostAddress, response, transport, authentication)
    }

    private fun sendConnectInitV2(
        init: ConnectInitV2,
        config: PairingConfig,
        hostAddress: String,
        discoveryPort: Int,
    ): ConnectOfferV2 {
        val address = InetAddress.getByName(hostAddress)
        DatagramSocket().use { socket ->
            socket.soTimeout = 5_000
            val request = init.canonicalJson().toByteArray(Charsets.UTF_8)
            socket.send(DatagramPacket(request, request.size, address, discoveryPort))
            val buffer = ByteArray(16 * 1024)
            while (true) {
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)
                val json = runCatching {
                    JSONObject(String(packet.data, packet.offset, packet.length, Charsets.UTF_8))
                }.getOrNull() ?: continue
                if (json.optionalString("type") != PairingV2Constants.ConnectOfferType) continue
                val offer = runCatching { ConnectOfferV2.fromJson(json) }.getOrNull() ?: continue
                if (runCatching { PairingV2OfferVerifier.verify(init, offer, config) }.isSuccess) {
                    return offer
                }
            }
        }
    }

    private fun buildWebSocketUrl(hostAddress: String, port: Int, path: String, token: String): String {
        val normalizedPath = if (path.startsWith("/")) path else "/$path"
        val encodedToken = URLEncoder.encode(token, "UTF-8")
        val normalizedHost = if (hostAddress.contains(":") && !hostAddress.startsWith("[")) "[$hostAddress]" else hostAddress
        return "ws://$normalizedHost:$port$normalizedPath?token=$encodedToken"
    }

    private fun buildWebSocketUrlV2(hostAddress: String, port: Int, path: String): String {
        val normalizedPath = if (path.startsWith("/")) path else "/$path"
        val normalizedHost = if (hostAddress.contains(":") && !hostAddress.startsWith("[")) "[$hostAddress]" else hostAddress
        return "wss://$normalizedHost:$port$normalizedPath"
    }
}

internal fun JSONObject.toCompactString(): String = toString()
