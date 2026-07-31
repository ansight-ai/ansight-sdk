package ai.ansight.runtime

import android.app.Application
import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.security.SecureRandom
import java.time.Instant
import java.time.OffsetDateTime
import java.util.Base64
import java.util.UUID
import java.util.zip.GZIPInputStream

object PairingProtocolDefaults {
    const val DiscoveryPort = 45_123
    const val DeveloperDiscoveryPort = 46_123
    const val WebSocketPort = 45_124
    const val WebSocketPath = "/ws"

    val LocalDiscoveryPorts = listOf(DiscoveryPort, DeveloperDiscoveryPort)
}

internal object PairingEnrollmentModes {
    const val Invite = "invite"
    const val Local = "local"
    const val LocalConfigPrefix = "local:"
}

data class PairingConfig(
    val schema: String,
    val configId: String,
    val appId: String,
    val appName: String,
    val issuedAt: String,
    val expiresAt: String,
    val minProtocolVersion: Int = 2,
    val allowedTransports: List<String> = listOf("ws"),
    val host: PairingHost,
    val enrollment: PairingEnrollment,
) {
    companion object {
        const val SchemaName = "ansight.enrollment-invite.v2"

        fun fromJson(json: JSONObject): PairingConfig = PairingConfig(
            schema = json.requiredString("schema"),
            configId = json.requiredString("inviteId"),
            appId = json.requiredString("appId"),
            appName = json.requiredString("appName"),
            issuedAt = json.requiredString("issuedAt"),
            expiresAt = json.requiredString("expiresAt"),
            minProtocolVersion = json.optInt("minProtocolVersion", 2),
            allowedTransports = json.optJSONArray("allowedTransports").toStringList(),
            host = PairingHost.fromJson(json.getJSONObject("host")),
            enrollment = PairingEnrollment.fromJson(json.getJSONObject("enrollment")),
        )
    }
}

data class PairingHost(
    val hostId: String?,
    val hostName: String?,
    val discoveryPort: Int,
) {
    companion object {
        fun fromJson(json: JSONObject): PairingHost = PairingHost(
            hostId = json.optionalString("hostId"),
            hostName = json.optionalString("hostName"),
            discoveryPort = json.optInt("discoveryPort", PairingProtocolDefaults.DiscoveryPort),
        )
    }
}

data class PairingEnrollment(
    val accessToken: String,
    val expiresAt: String,
    val grantExpiresAt: String,
    val maxUses: Int,
    val maxScopes: List<String>,
    val allowCritical: Boolean,
) {
    companion object {
        fun fromJson(json: JSONObject): PairingEnrollment = PairingEnrollment(
            accessToken = json.requiredString("accessToken"),
            expiresAt = json.requiredString("expiresAt"),
            grantExpiresAt = json.requiredString("grantExpiresAt"),
            maxUses = json.optInt("maxUses", 1),
            maxScopes = json.optJSONArray("maxScopes").toStringList(),
            allowCritical = json.optBoolean("allowCritical", false),
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
                    rawAddresses.optString(index).trim().ifBlank { null }?.let(addresses::add)
                }
            }

            json.optionalString("hostAddress")?.let(addresses::add)
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
    const val ConfigDocumentSchemaName = "ansight.enrollment-invite-document.v2"

    fun parseAndValidateDocument(payload: String, expectedAppId: String? = null): ParsedPairingDocument {
        val document = parseDocument(payload)
        validateDocument(document, expectedAppId)
        return document
    }

    fun parseDocument(payload: String): ParsedPairingDocument {
        val trimmed = payload.trim()
        require(trimmed.isNotEmpty()) { "Scan an Ansight enrollment QR code." }

        PairingConfigCodeGenerator.tryParse(trimmed)?.let { document -> return document }
        val root = try {
            JSONObject(trimmed)
        } catch (ex: Exception) {
            throw IllegalArgumentException("Failed to parse enrollment invite: ${ex.message}", ex)
        }

        return when (val schema = root.optionalString("schema").orEmpty()) {
            PairingConfig.SchemaName -> ParsedPairingDocument(PairingConfig.fromJson(root))
            ConfigDocumentSchemaName -> {
                val invite = root.optJSONObject("invite")
                    ?: throw IllegalArgumentException("Enrollment invite document is missing its invite.")
                ParsedPairingDocument(
                    config = PairingConfig.fromJson(invite),
                    discoveryHint = PairingDiscoveryHint.fromJson(root.optJSONObject("discovery")),
                )
            }
            "" -> throw IllegalArgumentException("The QR code is not an Ansight enrollment invite.")
            else -> throw IllegalArgumentException("Unsupported enrollment invite schema '$schema'.")
        }
    }

    fun validateDocument(document: ParsedPairingDocument, expectedAppId: String? = null) {
        val config = document.config
        require(config.schema == PairingConfig.SchemaName) {
            "Unsupported enrollment invite schema '${config.schema}'."
        }
        require(
            config.minProtocolVersion == 2 &&
                config.allowedTransports == listOf("ws") &&
                config.configId.isNotBlank() &&
                config.appId.isNotBlank() &&
                config.appName.isNotBlank() &&
                config.host.discoveryPort in 1..65_535 &&
                config.enrollment.maxUses == 1 &&
                decodeBase64Url(config.enrollment.accessToken)?.size == 32
        ) {
            "Enrollment invite is incomplete or uses an unsupported connection protocol."
        }

        val registrationExpiry = parseConfigInstant(config.enrollment.grantExpiresAt)
        require(!Instant.now().isAfter(registrationExpiry)) {
            "Device registration expired at ${config.enrollment.grantExpiresAt}. Scan a fresh QR code."
        }

        val normalizedExpected = expectedAppId?.trim().orEmpty()
        if (normalizedExpected.isNotEmpty()) {
            require(config.appId.trim() == normalizedExpected) {
                "Enrollment invite appId '${config.appId.trim()}' does not match expected app id '$normalizedExpected'."
            }
        }
    }

    internal fun parseConfigInstant(value: String): Instant =
        runCatching { Instant.parse(value) }
            .recoverCatching { OffsetDateTime.parse(value).toInstant() }
            .getOrElse { throw IllegalArgumentException("Enrollment expiry could not be parsed.", it) }
}

internal object PairingConfigCodeGenerator {
    const val FormatPrefix = "ans2"

    fun tryParse(payload: String): ParsedPairingDocument? {
        val normalizedPayload = payload.trim()
        if (!normalizedPayload.startsWith("$FormatPrefix:")) {
            return null
        }

        val encodedPayload = normalizedPayload.substring(FormatPrefix.length + 1)
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
            if (root.optionalString("schema") != PairingConfigDocumentService.ConfigDocumentSchemaName) {
                return null
            }

            val invite = root.optJSONObject("invite") ?: return null
            ParsedPairingDocument(
                config = PairingConfig.fromJson(invite),
                discoveryHint = PairingDiscoveryHint.fromJson(root.optJSONObject("discovery")),
            )
        }.getOrNull()
    }
}

data class ConnectRequest(
    val requestId: String,
    val enrollmentMode: String = PairingEnrollmentModes.Invite,
    val inviteId: String,
    val appId: String,
    val deviceId: String,
    val deviceName: String,
    val accessToken: String,
    val processSessionId: String? = ProcessSessionIdentity.current,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("type", "ENROLLMENT_CONNECT")
        .put("ver", 2)
        .put("requestId", requestId)
        .put("enrollmentMode", enrollmentMode)
        .put("inviteId", inviteId)
        .put("appId", appId)
        .put("deviceId", deviceId)
        .put("deviceName", deviceName)
        .put("accessToken", accessToken)
        .putIfNotNull("processSessionId", processSessionId)
}

data class ConnectResponse(
    val type: String,
    val ver: Int,
    val requestId: String,
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
            ver = json.optInt("ver", 0),
            requestId = json.requiredString("requestId"),
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
    val current: String = "android.${UUID.randomUUID().toString().replace("-", "")}"
}

internal object AndroidPairingDeviceIdentity {
    private const val PreferencesName = "ai.ansight.enrollment"
    private const val DeviceIdKey = "deviceId"
    private const val AccessTokenKey = "localAccessToken"
    private val processFallback = "android.${UUID.randomUUID().toString().replace("-", "")}"
    private val processAccessTokenFallback = createAccessToken()

    fun resolve(application: Application?): String {
        if (application == null) {
            return processFallback
        }

        val preferences = application.getSharedPreferences(PreferencesName, Application.MODE_PRIVATE)
        synchronized(this) {
            preferences.getString(DeviceIdKey, null)?.trim()?.ifBlank { null }?.let { return it }
            val deviceId = "android.${UUID.randomUUID().toString().replace("-", "")}"
            preferences.edit().putString(DeviceIdKey, deviceId).apply()
            return deviceId
        }
    }

    fun resolveAccessToken(application: Application?): String {
        if (application == null) {
            return processAccessTokenFallback
        }

        val preferences = application.getSharedPreferences(PreferencesName, Application.MODE_PRIVATE)
        synchronized(this) {
            preferences.getString(AccessTokenKey, null)?.trim()?.ifBlank { null }?.let { return it }
            return createAccessToken().also { accessToken ->
                preferences.edit().putString(AccessTokenKey, accessToken).apply()
            }
        }
    }

    private fun createAccessToken(): String {
        val bytes = ByteArray(32)
        SecureRandom().nextBytes(bytes)
        return Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)
    }
}

internal object LocalPairingDocumentFactory {
    fun createPayload(
        application: Application,
        appName: String,
        hostAddress: String,
        discoveryPort: Int,
    ): String {
        val now = Instant.now()
        val expiresAt = now.plusSeconds(10L * 365L * 24L * 60L * 60L).toString()
        val invite = JSONObject()
            .put("schema", PairingConfig.SchemaName)
            .put("inviteId", "${PairingEnrollmentModes.LocalConfigPrefix}${application.packageName}")
            .put("appId", application.packageName)
            .put("appName", appName)
            .put("issuedAt", now.toString())
            .put("expiresAt", expiresAt)
            .put("minProtocolVersion", 2)
            .put("allowedTransports", JSONArray().put("ws"))
            .put(
                "host",
                JSONObject()
                    .put("hostName", "Local Ansight Studio")
                    .put("discoveryPort", discoveryPort),
            )
            .put(
                "enrollment",
                JSONObject()
                    .put("accessToken", AndroidPairingDeviceIdentity.resolveAccessToken(application))
                    .put("expiresAt", expiresAt)
                    .put("grantExpiresAt", expiresAt)
                    .put("maxUses", 1)
                    .put("maxScopes", JSONArray().put("Read"))
                    .put("allowCritical", false),
            )
        val discovery = JSONObject()
            .put("schema", "ansight.discovery-hint.v1")
            .put("source", "runtime-local")
            .put("hostAddresses", JSONArray().put(hostAddress))
            .put("discoveryPort", discoveryPort)
            .put("hostName", "Local Ansight Studio")
            .put("capturedAt", now.toString())
        return JSONObject()
            .put("schema", PairingConfigDocumentService.ConfigDocumentSchemaName)
            .put("invite", invite)
            .put("discovery", discovery)
            .toString()
    }
}

internal object PairingSimulatorLocalHostAddress {
    fun resolve(): String? = runCatching {
        if (isAndroidEmulator()) androidHostAddress() else null
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
    val allowCellularConnections: Boolean = false,
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
        fun failure(message: String, code: String? = null) =
            PairingConnectionAttempt(false, false, message, failureCode = code)

        fun rejected(hostAddress: String, response: ConnectResponse) = PairingConnectionAttempt(
            success = false,
            accepted = false,
            message = response.reasonMessage ?: response.message,
            hostAddress = hostAddress,
            connectResponse = response,
        )

        fun success(hostAddress: String, response: ConnectResponse, transport: PairingLiveSessionTransport) =
            PairingConnectionAttempt(
                success = true,
                accepted = true,
                message = "Connected to Studio.",
                hostAddress = hostAddress,
                connectResponse = response,
                transport = transport,
            )
    }
}

class PairingSessionConnector(
    private val simulatorLocalHostAddressProvider: () -> String? = { PairingSimulatorLocalHostAddress.resolve() },
    private val applicationProvider: () -> Application? = { null },
) {
    private var networkStatusProvider: () -> PairingNetworkPreflightStatus =
        { PairingNetworkPreflightStatus.Unknown }

    internal constructor(
        simulatorLocalHostAddressProvider: () -> String?,
        networkStatusProvider: () -> PairingNetworkPreflightStatus,
        applicationProvider: () -> Application? = { null },
    ) : this(simulatorLocalHostAddressProvider, applicationProvider) {
        this.networkStatusProvider = networkStatusProvider
    }

    internal fun localHostAddress(): String? = resolveSimulatorLocalHostAddress()

    fun connect(
        document: ParsedPairingDocument,
        clientName: String,
        options: PairingConnectionOptions = PairingConnectionOptions(),
    ): PairingConnectionAttempt {
        val simulatorLocalHostAddress = resolveSimulatorLocalHostAddress()
        val hostAddressCandidates = PairingHostAddressCandidates.resolve(
            document.discoveryHint,
            options.hostAddressOverride,
            simulatorLocalHostAddress,
        )
        if (hostAddressCandidates.isEmpty()) {
            return PairingConnectionAttempt.failure(
                "The scanned Ansight QR code does not contain a reachable Studio address.",
                PairingFailureCodes.HostAddressRequired,
            )
        }

        val discoveryPort = options.discoveryPort
            ?: document.discoveryHint?.discoveryPort
            ?: document.config.host.discoveryPort
        if (discoveryPort !in 1..65_535) {
            return PairingConnectionAttempt.failure(
                "Studio discovery port must be between 1 and 65535.",
                PairingFailureCodes.HostAddressRequired,
            )
        }

        val hasSimulatorLocalHostCandidate = simulatorLocalHostAddress != null &&
            hostAddressCandidates.any { it.equals(simulatorLocalHostAddress, ignoreCase = true) }
        val networkStatus = if (hasSimulatorLocalHostCandidate) {
            PairingNetworkPreflightStatus.Connected
        } else {
            networkStatusProvider()
        }
        if (networkStatus == PairingNetworkPreflightStatus.NotConnected) {
            return PairingConnectionAttempt.failure(
                "This device must be on the same Wi-Fi network as Ansight Studio.",
                PairingFailureCodes.WifiRequired,
            )
        }
        if (networkStatus == PairingNetworkPreflightStatus.Cellular && !options.allowCellularConnections) {
            return PairingConnectionAttempt.failure(
                "Cellular Studio connections are disabled.",
                PairingFailureCodes.WifiRequired,
            )
        }

        val deviceId = AndroidPairingDeviceIdentity.resolve(applicationProvider())
        var lastFailure: PairingConnectionAttempt? = null
        for (hostAddress in hostAddressCandidates) {
            val connectResponse = try {
                sendConnectRequest(document.config, clientName, deviceId, hostAddress, discoveryPort)
            } catch (ex: Exception) {
                lastFailure = PairingConnectionAttempt.failure(
                    "UDP enrollment failed for $hostAddress: ${ex.message}",
                    PairingFailureCodes.UdpBootstrapFailed,
                )
                continue
            }

            if (connectResponse == null) {
                lastFailure = PairingConnectionAttempt.failure(
                    "No response from Studio at $hostAddress. Scan a fresh QR code if Studio's address changed.",
                    PairingFailureCodes.UdpBootstrapTimeout,
                )
                continue
            }
            if (!connectResponse.accepted) {
                return PairingConnectionAttempt.rejected(hostAddress, connectResponse)
            }

            val webSocketPort = connectResponse.webSocketPort
            val webSocketPath = connectResponse.webSocketPath?.trim()
            val webSocketToken = connectResponse.webSocketToken?.trim()
            if (webSocketPort == null || webSocketPath.isNullOrBlank() || webSocketToken.isNullOrBlank()) {
                return PairingConnectionAttempt.failure(
                    "Studio did not provide a WebSocket handoff.",
                    PairingFailureCodes.WebSocketHandoffUnavailable,
                )
            }

            val transport = PairingLiveSessionTransport()
            val openResult = transport.open(buildWebSocketUrl(hostAddress, webSocketPort, webSocketPath, webSocketToken))
            if (!openResult.success) {
                return PairingConnectionAttempt.failure(
                    openResult.message,
                    PairingFailureCodes.WebSocketEndpointUnreachable,
                )
            }

            return PairingConnectionAttempt.success(hostAddress, connectResponse, transport)
        }

        return lastFailure ?: PairingConnectionAttempt.failure(
            "The scanned Ansight QR code does not contain a reachable Studio address.",
            PairingFailureCodes.HostAddressRequired,
        )
    }

    private fun resolveSimulatorLocalHostAddress(): String? =
        runCatching { simulatorLocalHostAddressProvider()?.trim()?.ifBlank { null } }.getOrNull()

    private fun sendConnectRequest(
        config: PairingConfig,
        deviceName: String,
        deviceId: String,
        hostAddress: String,
        discoveryPort: Int,
    ): ConnectResponse? {
        val address = InetAddress.getByName(hostAddress)
        val requestId = UUID.randomUUID().toString().replace("-", "")
        DatagramSocket().use { socket ->
            val isLocalEnrollment = config.configId.startsWith(PairingEnrollmentModes.LocalConfigPrefix)
            socket.soTimeout = if (isLocalEnrollment) 1_000 else 5_000
            val request = ConnectRequest(
                requestId = requestId,
                enrollmentMode = if (isLocalEnrollment) {
                    PairingEnrollmentModes.Local
                } else {
                    PairingEnrollmentModes.Invite
                },
                inviteId = config.configId,
                appId = config.appId,
                deviceId = deviceId,
                deviceName = deviceName,
                accessToken = config.enrollment.accessToken,
            ).toJson().toString().toByteArray(StandardCharsets.UTF_8)

            socket.send(DatagramPacket(request, request.size, address, discoveryPort))
            val buffer = ByteArray(16 * 1024)
            while (true) {
                val packet = DatagramPacket(buffer, buffer.size)
                socket.receive(packet)
                if (packet.address != address) {
                    continue
                }

                val response = ConnectResponse.fromJson(
                    JSONObject(String(packet.data, packet.offset, packet.length, StandardCharsets.UTF_8)),
                )
                require(
                    response.type == "ENROLLMENT_RESULT" &&
                        response.ver == 2 &&
                        response.requestId == requestId
                ) {
                    "Studio returned an unexpected enrollment response."
                }
                return response
            }
        }
    }

    private fun buildWebSocketUrl(hostAddress: String, port: Int, path: String, token: String): String {
        val normalizedPath = if (path.startsWith("/")) path else "/$path"
        val encodedToken = URLEncoder.encode(token, "UTF-8")
        val normalizedHost = if (hostAddress.contains(":") && !hostAddress.startsWith("[")) {
            "[$hostAddress]"
        } else {
            hostAddress
        }
        return "ws://$normalizedHost:$port$normalizedPath?token=$encodedToken"
    }
}

internal fun JSONObject.toCompactString(): String = toString()

private fun JSONArray?.toStringList(): List<String> {
    if (this == null) {
        return emptyList()
    }

    return (0 until length()).mapNotNull { index ->
        optString(index).trim().ifBlank { null }
    }
}

internal fun decodeBase64Url(value: String?): ByteArray? =
    value?.trim()?.ifBlank { null }?.let { encoded ->
        runCatching { Base64.getUrlDecoder().decode(encoded) }.getOrNull()
    }
