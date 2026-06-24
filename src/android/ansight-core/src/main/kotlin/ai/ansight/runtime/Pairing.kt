package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.security.KeyFactory
import java.security.Signature
import java.security.spec.X509EncodedKeySpec
import java.time.Instant
import java.time.OffsetDateTime
import java.util.Base64
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
    val oneTimeToken: String,
    val host: PairingHost,
    val challenge: PairingChallenge,
    val signature: String,
) {
    companion object {
        const val SchemaName = "ansight.pairing-config.v1"

        fun fromJson(json: JSONObject): PairingConfig = PairingConfig(
            schema = json.requiredString("schema"),
            configId = json.requiredString("configId"),
            appId = json.requiredString("appId"),
            appName = json.requiredString("appName"),
            issuedAt = json.requiredString("issuedAt"),
            expiresAt = json.requiredString("expiresAt"),
            oneTimeToken = json.requiredString("oneTimeToken"),
            host = PairingHost.fromJson(json.getJSONObject("host")),
            challenge = PairingChallenge.fromJson(json.getJSONObject("challenge")),
            signature = json.requiredString("signature"),
        )
    }
}

data class PairingHost(
    val hostId: String?,
    val hostName: String?,
    val discoveryPort: Int,
    val hostPubKey: String,
    val hostPubKeyFingerprint: String,
) {
    companion object {
        fun fromJson(json: JSONObject): PairingHost = PairingHost(
            hostId = json.optionalString("hostId"),
            hostName = json.optionalString("hostName"),
            discoveryPort = json.optionalInt("discoveryPort") ?: PairingProtocolDefaults.DiscoveryPort,
            hostPubKey = json.requiredString("hostPubKey"),
            hostPubKeyFingerprint = json.requiredString("hostPubKeyFingerprint"),
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
)

object PairingConfigDocumentService {
    const val ConfigDocumentSchemaName = "ansight.pairing-config-document.v1"
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

        if (schema == PairingConfig.SchemaName) {
            return ParsedPairingDocument(PairingConfig.fromJson(root))
        }

        if (schema == ConfigDocumentSchemaName || schema == LegacySchemaName) {
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
        require(config.schema == PairingConfig.SchemaName) {
            "Unsupported pairing config schema '${config.schema}'."
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
            val publicKeyBytes = Base64.getDecoder().decode(config.host.hostPubKey)
            val signatureBytes = Base64.getDecoder().decode(config.signature)
            val publicKey = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(publicKeyBytes))
            val derSignature = ensureDerSignature(signatureBytes)
            listOf(
                PairingCanonicalJson.serializePairingConfigForSignature(config),
                PairingCanonicalJson.serializePairingConfigWithLegacyTrustForSignature(config),
            ).any { signable ->
                val verifier = Signature.getInstance("SHA256withECDSA")
                verifier.initVerify(publicKey)
                verifier.update(signable.toByteArray(StandardCharsets.UTF_8))
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
    const val LegacyFormatPrefix = "apt1"

    fun tryParse(payload: String): ParsedPairingDocument? {
        val normalizedPayload = payload.trim()
        val formatPrefix = when {
            normalizedPayload.startsWith("$FormatPrefix:") -> FormatPrefix
            normalizedPayload.startsWith("$LegacyFormatPrefix:") -> LegacyFormatPrefix
            else -> return null
        }

        val encodedPayload = normalizedPayload.substring(formatPrefix.length + 1)
        val compressedBytes = runCatching {
            Base64.getUrlDecoder().decode(encodedPayload)
        }.getOrNull() ?: return null

        val json = runCatching {
            GZIPInputStream(compressedBytes.inputStream()).use { gzip ->
                String(gzip.readBytes(), StandardCharsets.UTF_8)
            }
        }.getOrNull() ?: return null

        return runCatching {
            val root = JSONObject(json)
            val schema = root.optionalString("schema")
            if (schema != PairingConfigDocumentService.ConfigDocumentSchemaName &&
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

    private fun serializePairingConfig(config: PairingConfig, includeLegacyTrust: Boolean): String {
        val hostJson = serializeHost(config.host.hostPubKey, config.host.hostPubKeyFingerprint)
        val fields = mutableListOf(
            jsonStringField("schema", config.schema, escapePlus = true),
            jsonStringField("configId", config.configId, escapePlus = true),
            jsonStringField("appId", config.appId, escapePlus = true),
            jsonStringField("appName", config.appName, escapePlus = true),
            jsonStringField("issuedAt", config.issuedAt, escapePlus = false),
            jsonStringField("expiresAt", config.expiresAt, escapePlus = false),
            jsonStringField("oneTimeToken", config.oneTimeToken, escapePlus = true),
            "\"host\":$hostJson",
            "\"challenge\":${serializeChallenge(config.challenge)}",
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
                else -> {
                    val code = char.code
                    if (code < 0x20) {
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

data class PairingConnectionOptions(
    val hostAddressOverride: String? = null,
    val discoveryPort: Int? = null,
)

data class PairingConnectionAttempt(
    val success: Boolean,
    val accepted: Boolean,
    val message: String,
    val hostAddress: String? = null,
    val connectResponse: ConnectResponse? = null,
    val transport: PairingLiveSessionTransport? = null,
    val failureCode: String? = null,
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
        fun success(hostAddress: String, response: ConnectResponse, transport: PairingLiveSessionTransport) = PairingConnectionAttempt(
            success = true,
            accepted = true,
            message = "Connected to host and WebSocket session is ready.",
            hostAddress = hostAddress,
            connectResponse = response,
            transport = transport,
        )
    }
}

class PairingSessionConnector {
    fun connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions = PairingConnectionOptions(),
    ): PairingConnectionAttempt {
        val hostAddressCandidates = options.hostAddressOverride?.trim()?.ifBlank { null }?.let { listOf(it) }
            ?: document.discoveryHint?.hostAddressCandidates.orEmpty()
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
                oneTimeToken = config.oneTimeToken,
                appId = config.appId,
                clientName = clientName,
            ).toJson().toString().toByteArray(StandardCharsets.UTF_8)

            it.send(DatagramPacket(request, request.size, address, discoveryPort))
            val buffer = ByteArray(16 * 1024)
            val packet = DatagramPacket(buffer, buffer.size)
            while (true) {
                it.receive(packet)
                if (packet.address != address) {
                    continue
                }

                val json = JSONObject(String(packet.data, packet.offset, packet.length, StandardCharsets.UTF_8))
                return ConnectResponse.fromJson(json)
            }
        }
    }

    private fun buildWebSocketUrl(hostAddress: String, port: Int, path: String, token: String): String {
        val normalizedPath = if (path.startsWith("/")) path else "/$path"
        val encodedToken = URLEncoder.encode(token, "UTF-8")
        val normalizedHost = if (hostAddress.contains(":") && !hostAddress.startsWith("[")) "[$hostAddress]" else hostAddress
        return "ws://$normalizedHost:$port$normalizedPath?token=$encodedToken"
    }
}

internal fun JSONObject.toCompactString(): String = toString()
