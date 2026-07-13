package ai.ansight.runtime

import org.json.JSONObject
import java.security.SecureRandom

internal object ConnectInitV2Factory {
    private val random = SecureRandom()

    fun create(config: PairingConfig): ConnectInitV2 = ConnectInitV2(
        requestId = randomBase64Url(16),
        configId = config.configId,
        appId = config.appId,
        clientNonce = randomBase64Url(32),
    )

    private fun randomBase64Url(size: Int): String = ByteArray(size).also(random::nextBytes).let(PairingV2Crypto::encodeBase64Url)
}

internal object PairingV2OfferVerifier {
    fun verify(init: ConnectInitV2, offer: ConnectOfferV2, config: PairingConfig, nowEpochMillis: Long = System.currentTimeMillis()) {
        require(offer.type == PairingV2Constants.ConnectOfferType && offer.ver == 2) { "Host did not return a protocol v2 connection offer." }
        require(
            offer.requestId == init.requestId &&
                offer.configId == init.configId &&
                offer.appId == init.appId &&
                offer.clientNonce == init.clientNonce
        ) { "Protocol v2 offer did not echo the bootstrap request." }
        PairingV2Crypto.decodeBase64Url(init.requestId, 16)
        PairingV2Crypto.decodeBase64Url(init.clientNonce, 32)
        PairingV2Crypto.decodeBase64Url(offer.hostNonce, 32)
        require(offer.hostId == config.host.hostId) { "Protocol v2 offer hostId does not match the paired host." }
        require(offer.selectedVersion == 2 && offer.selectedTransport == PairingV2Constants.Transport) {
            "Protocol v2 offer selected an insecure or unsupported protocol."
        }
        require(
            offer.webSocketPort in 1..65_535 &&
                offer.webSocketPath.startsWith("/ws/v2/") &&
                !offer.webSocketPath.contains('?') &&
                !offer.webSocketPath.contains('#')
        ) {
            "Protocol v2 offer contains an invalid WSS endpoint."
        }
        require(offer.signatureAlgorithm == PairingV2Constants.SignatureAlgorithm) { "Protocol v2 offer signature algorithm is unsupported." }
        val expiresAt = PairingV2ConfigValidator.parseTimestamp(offer.expiresAt, "offer expiresAt")
        require(
            expiresAt > nowEpochMillis - PairingV2Constants.MaximumClockSkewMillis &&
                expiresAt <= nowEpochMillis + PairingV2Constants.MaximumOfferLifetimeMillis + PairingV2Constants.MaximumClockSkewMillis
        ) {
            "Protocol v2 offer is expired or has an excessive lifetime."
        }
        val matchingPin = config.host.tlsPins.firstOrNull { pin ->
            PairingV2ConfigValidator.isPinCurrent(pin, nowEpochMillis) &&
                PairingV2Crypto.fixedTimeEquals(
                    PairingV2Crypto.decodeBase64Url(pin.tlsSpkiSha256, 32),
                    PairingV2Crypto.decodeBase64Url(offer.tlsSpkiSha256, 32),
                )
        }
        require(matchingPin != null) { "Protocol v2 offer TLS pin is not currently authorized by the pairing config." }
        val signed = "ANSIGHT-CONNECT-OFFER-V2\n${init.canonicalJson()}\n${offer.canonicalJson()}"
        require(PairingV2Crypto.verifyP1363(PairingV2Crypto.publicKey(config.host.hostPubKey), signed, offer.signature)) {
            "Protocol v2 offer signature is invalid."
        }
    }
}

data class PairingV2AuthenticationResult(
    val sessionId: String,
    val clientKeyId: String,
    val grant: PairingGrantV2,
)

internal interface PairingV2AuthenticationChannel {
    fun awaitTextMessage(timeoutMilliseconds: Long): String?
    fun sendText(text: String): OperationResult
}

class PairingV2Authenticator internal constructor(
    private val clientKeyProvider: PairingClientKeyProvider = AndroidPairingClientKeyProvider,
    private val nowProvider: () -> Long = System::currentTimeMillis,
) {
    internal fun authenticate(
        transport: PairingLiveSessionTransport,
        document: ParsedPairingDocument,
        init: ConnectInitV2,
        offer: ConnectOfferV2,
        requestedScopes: List<String>,
        requestCritical: Boolean,
        timeoutMilliseconds: Long = 10_000,
    ): PairingV2AuthenticationResult = authenticate(
        channel = object : PairingV2AuthenticationChannel {
            override fun awaitTextMessage(timeoutMilliseconds: Long): String? = transport.awaitTextMessage(timeoutMilliseconds)
            override fun sendText(text: String): OperationResult = transport.sendText(text)
        },
        document = document,
        init = init,
        offer = offer,
        requestedScopes = requestedScopes,
        requestCritical = requestCritical,
        timeoutMilliseconds = timeoutMilliseconds,
    )

    internal fun authenticate(
        channel: PairingV2AuthenticationChannel,
        document: ParsedPairingDocument,
        init: ConnectInitV2,
        offer: ConnectOfferV2,
        requestedScopes: List<String>,
        requestCritical: Boolean,
        timeoutMilliseconds: Long = 10_000,
    ): PairingV2AuthenticationResult {
        val config = document.config
        val challengeText = channel.awaitTextMessage(timeoutMilliseconds)
            ?: throw IllegalArgumentException("Timed out waiting for the protocol v2 authentication challenge.")
        val challengeJson = JSONObject(challengeText)
        if (challengeJson.optionalString("type") == PairingV2Constants.AuthErrorType) {
            throw authError(challengeJson)
        }
        val challenge = AuthChallengeV2.fromJson(challengeJson)
        validateChallenge(challenge, init, offer, config, nowProvider())

        val clientKey = clientKeyProvider.getOrCreate("${config.host.hostId}|${config.appId}")
        document.clientKeyId?.let { rememberedKeyId ->
            require(rememberedKeyId == clientKey.keyId) { "Remembered protocol v2 client key is unavailable or has changed." }
        }

        val grant = document.grant
        val response = if (grant == null) {
            createEnrollmentResponse(config, init, offer, challenge, clientKey, requestedScopes, requestCritical)
        } else {
            PairingV2GrantVerifier.verify(grant, config, clientKey.keyId, nowProvider())
            createReconnectResponse(init, offer, challenge, clientKey, grant)
        }
        val sendResult = channel.sendText(response.toString())
        require(sendResult.success) { sendResult.message }

        val resultText = channel.awaitTextMessage(timeoutMilliseconds)
            ?: throw IllegalArgumentException("Timed out waiting for the protocol v2 authentication result.")
        val resultJson = JSONObject(resultText)
        if (resultJson.optionalString("type") == PairingV2Constants.AuthErrorType) {
            throw authError(resultJson)
        }
        val result = AuthOkV2.fromJson(resultJson)
        PairingV2Crypto.decodeBase64Url(result.sessionId)
        PairingV2GrantVerifier.verify(result.grant, config, clientKey.keyId, nowProvider())
        val normalizedRequestedScopes = PairingV2Scopes.normalize(requestedScopes)
        require(result.grant.allowedScopes.all(normalizedRequestedScopes::contains)) {
            "Protocol v2 grant exceeds the scopes requested by the client."
        }
        require(!result.grant.allowCritical || requestCritical) { "Protocol v2 grant unexpectedly permits critical operations." }
        config.enrollment?.let { enrollment ->
            require(
                PairingV2ConfigValidator.parseTimestamp(result.grant.expiresAt, "grant expiresAt") <=
                    PairingV2ConfigValidator.parseTimestamp(enrollment.grantExpiresAt, "enrollment.grantExpiresAt")
            ) { "Protocol v2 grant exceeds the enrollment expiry." }
        }
        return PairingV2AuthenticationResult(result.sessionId, clientKey.keyId, result.grant)
    }

    private fun validateChallenge(
        challenge: AuthChallengeV2,
        init: ConnectInitV2,
        offer: ConnectOfferV2,
        config: PairingConfig,
        nowEpochMillis: Long = System.currentTimeMillis(),
    ) {
        require(
            challenge.requestId == init.requestId &&
                challenge.configId == init.configId &&
                challenge.appId == init.appId &&
                challenge.clientNonce == init.clientNonce &&
                challenge.hostNonce == offer.hostNonce
        ) { "Protocol v2 authentication challenge is not bound to the signed offer." }
        PairingV2Crypto.decodeBase64Url(challenge.authSessionId, 16)
        PairingV2Crypto.decodeBase64Url(challenge.serverChallenge, 32)
        val expiresAt = PairingV2ConfigValidator.parseTimestamp(challenge.expiresAt, "authentication challenge expiresAt")
        require(
            expiresAt > nowEpochMillis - PairingV2Constants.MaximumClockSkewMillis &&
                expiresAt <= nowEpochMillis + PairingV2Constants.MaximumChallengeLifetimeMillis + PairingV2Constants.MaximumClockSkewMillis
        ) {
            "Protocol v2 authentication challenge is expired or has an excessive lifetime."
        }
        require(config.host.hostId == offer.hostId) { "Protocol v2 challenge host binding is invalid." }
    }

    private fun createEnrollmentResponse(
        config: PairingConfig,
        init: ConnectInitV2,
        offer: ConnectOfferV2,
        challenge: AuthChallengeV2,
        clientKey: PairingClientKey,
        requestedScopes: List<String>,
        requestCritical: Boolean,
    ): JSONObject {
        val enrollment = requireNotNull(config.enrollment) { "Protocol v2 enrollment material has already been removed." }
        val normalizedScopes = PairingV2Scopes.normalize(requestedScopes).filter(PairingV2Scopes.normalize(enrollment.maxScopes)::contains)
        val critical = requestCritical && enrollment.allowCritical
        val signatureBytes = PairingV2Crypto.decodePaddedBase64(config.signature, 64)
        val configSignatureSha256 = PairingV2Crypto.sha256Base64Url(signatureBytes)
        val proofInput = PairingV2ProofInputs.enrollment(
            configSignatureSha256,
            init,
            offer,
            challenge,
            enrollment,
            clientKey.keyId,
            clientKey.publicKeyBase64,
            normalizedScopes,
            critical,
        )
        val secret = PairingV2Crypto.decodeBase64Url(enrollment.secret, 32)
        return try {
            AuthEnrollV2(
                authSessionId = challenge.authSessionId,
                ticketId = enrollment.ticketId,
                clientKeyId = clientKey.keyId,
                clientPublicKey = clientKey.publicKeyBase64,
                requestedScopes = normalizedScopes,
                requestCritical = critical,
                proof = PairingV2Crypto.encodeBase64Url(PairingV2Crypto.hmacSha256(secret, proofInput)),
            ).toJson()
        } finally {
            secret.fill(0)
        }
    }

    private fun createReconnectResponse(
        init: ConnectInitV2,
        offer: ConnectOfferV2,
        challenge: AuthChallengeV2,
        clientKey: PairingClientKey,
        grant: PairingGrantV2,
    ): JSONObject {
        val proof = PairingV2ProofInputs.reconnect(init, offer, challenge, grant.grantId, clientKey.keyId)
        return AuthProveV2(
            authSessionId = challenge.authSessionId,
            grantId = grant.grantId,
            clientKeyId = clientKey.keyId,
            signature = clientKey.signP1363(proof),
        ).toJson()
    }

    private fun authError(json: JSONObject): IllegalArgumentException {
        val error = AuthErrorV2.fromJson(json)
        return IllegalArgumentException("Protocol v2 authentication failed (${error.code}): ${PairingRedaction.redact(error.message)}")
    }
}
