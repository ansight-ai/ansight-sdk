package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test
import java.nio.charset.StandardCharsets
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.Signature
import java.security.spec.ECGenParameterSpec
import java.time.Instant
import java.util.Base64

class PairingV2ProtocolTest {
    private val now = Instant.parse("2026-07-13T00:00:00Z")

    @Test
    fun signedV2ConfigValidatesAllSecurityBindings() {
        val fixture = fixture()

        PairingV2ConfigValidator.validate(ParsedPairingDocument(fixture.config), "com.example.app", now.toEpochMilli())

        val tampered = fixture.config.copy(
            host = fixture.config.host.copy(hostPubKeyFingerprint = base64Url(ByteArray(32) { 7 })),
        )
        assertThrows(IllegalArgumentException::class.java) {
            PairingV2ConfigValidator.validate(ParsedPairingDocument(tampered), "com.example.app", now.toEpochMilli())
        }
        assertThrows(IllegalArgumentException::class.java) {
            PairingV2ConfigValidator.validate(
                ParsedPairingDocument(fixture.config.copy(allowedTransports = listOf("wss", "ws"))),
                "com.example.app",
                now.toEpochMilli(),
            )
        }
    }

    @Test
    fun parsesSecureV2ConfigDocumentWithoutTreatingItAsV1() {
        val fixture = fixture()
        val canonical = PairingCanonicalJson.serializePairingConfigV2ForSignature(fixture.config)
        val configJson = canonical.dropLast(1) + ",\"signature\":\"${fixture.config.signature}\"}"
        val documentJson = org.json.JSONObject()
            .put("schema", PairingConfigDocumentService.SecureConfigDocumentSchemaName)
            .put("config", org.json.JSONObject(configJson))
            .put("discovery", org.json.JSONObject().put("hostAddresses", org.json.JSONArray().put("127.0.0.1")))
            .toString()

        val document = PairingConfigDocumentService.parseDocument(documentJson)

        assertEquals(PairingConfig.SecureSchemaName, document.config.schema)
        assertEquals(2, document.config.minProtocolVersion)
        assertEquals(listOf("wss"), document.config.allowedTransports)
        assertEquals(32, PairingV2Crypto.decodeBase64Url(document.config.enrollment!!.secret).size)
    }

    @Test
    fun canonicalChallengeEscapesPositiveTimestampOffsetLikeSystemTextJson() {
        val challenge = AuthChallengeV2(
            authSessionId = "auth",
            requestId = "request",
            configId = "config",
            appId = "app",
            clientNonce = "client",
            hostNonce = "host",
            serverChallenge = "challenge",
            expiresAt = "2026-07-13T10:00:00.0000000+10:00",
        )

        assertEquals(
            "{\"type\":\"AUTH_CHALLENGE_V2\",\"ver\":2,\"authSessionId\":\"auth\",\"requestId\":\"request\",\"configId\":\"config\",\"appId\":\"app\",\"clientNonce\":\"client\",\"hostNonce\":\"host\",\"serverChallenge\":\"challenge\",\"expiresAt\":\"2026-07-13T10:00:00.0000000\\u002B10:00\"}",
            challenge.canonicalJson(),
        )
    }

    @Test
    fun connectInitIsSecretFreeAndOfferIsBoundToTheExactNonce() {
        val fixture = fixture()
        val init = ConnectInitV2(
            requestId = base64Url(ByteArray(16) { 1 }),
            configId = fixture.config.configId,
            appId = fixture.config.appId,
            clientNonce = base64Url(ByteArray(32) { 2 }),
        )
        assertFalse(init.canonicalJson().contains(fixture.config.enrollment!!.secret))
        assertEquals(
            "{\"type\":\"CONNECT_INIT_V2\",\"ver\":2,\"requestId\":\"AQEBAQEBAQEBAQEBAQEBAQ\",\"configId\":\"cfg-v2\",\"appId\":\"com.example.app\",\"clientNonce\":\"AgICAgICAgICAgICAgICAgICAgICAgICAgICAgICAgI\",\"supportedVersions\":[2],\"supportedTransports\":[\"wss\"]}",
            init.canonicalJson(),
        )

        val offer = signOffer(fixture, init)
        PairingV2OfferVerifier.verify(init, offer, fixture.config, now.toEpochMilli())

        assertThrows(IllegalArgumentException::class.java) {
            PairingV2OfferVerifier.verify(init.copy(clientNonce = base64Url(ByteArray(32) { 3 })), offer, fixture.config, now.toEpochMilli())
        }
    }

    @Test
    fun enrollmentProofUsesDecodedSecretAndRememberedProfileDeletesIt() {
        val fixture = fixture()
        val enrollment = requireNotNull(fixture.config.enrollment)
        val init = ConnectInitV2(
            requestId = base64Url(ByteArray(16) { 1 }),
            configId = fixture.config.configId,
            appId = fixture.config.appId,
            clientNonce = base64Url(ByteArray(32) { 2 }),
        )
        val offer = signOffer(fixture, init)
        val challenge = AuthChallengeV2(
            authSessionId = base64Url(ByteArray(16) { 4 }),
            requestId = init.requestId,
            configId = init.configId,
            appId = init.appId,
            clientNonce = init.clientNonce,
            hostNonce = offer.hostNonce,
            serverChallenge = base64Url(ByteArray(32) { 5 }),
            expiresAt = now.plusSeconds(20).toString(),
        )
        val clientKey = ecKeyPair()
        val clientKeyId = PairingV2Crypto.sha256Base64Url(clientKey.public.encoded)
        val clientPublicKey = Base64.getEncoder().encodeToString(clientKey.public.encoded)
        val input = PairingV2ProofInputs.enrollment(
            configSignatureSha256 = PairingV2Crypto.sha256Base64Url(Base64.getDecoder().decode(fixture.config.signature)),
            init = init,
            offer = offer,
            challenge = challenge,
            enrollment = enrollment,
            clientKeyId = clientKeyId,
            clientPublicKey = clientPublicKey,
            requestedScopes = listOf("Read"),
            requestCritical = false,
        )
        val secret = PairingV2Crypto.decodeBase64Url(enrollment.secret, 32)
        assertEquals(32, PairingV2Crypto.hmacSha256(secret, input).size)

        val grant = signedGrant(fixture, clientKeyId)
        val remembered = PairingRememberedProfileV2.create(
            ParsedPairingDocument(fixture.config, PairingDiscoveryHint(hostAddresses = listOf("127.0.0.1"))),
            clientKeyId,
            grant,
        )
        val persisted = remembered.toJson()
        assertFalse(persisted.contains(enrollment.secret))
        assertFalse(persisted.contains("ticketId"))
        PairingV2ConfigValidator.validate(
            PairingRememberedProfileV2.fromJson(org.json.JSONObject(persisted)).toParsedDocument(),
            fixture.config.appId,
            now.toEpochMilli(),
        )
    }

    @Test
    fun reconnectSignatureIsP1363AndVerifiesAgainstClientKey() {
        val keyPair = ecKeyPair()
        val clientKey = PairingClientKey(
            keyId = PairingV2Crypto.sha256Base64Url(keyPair.public.encoded),
            publicKeyBase64 = Base64.getEncoder().encodeToString(keyPair.public.encoded),
            persistent = true,
        ) { content ->
            Signature.getInstance("SHA256withECDSA").run {
                initSign(keyPair.private)
                update(content)
                sign()
            }
        }
        val proof = "{\"context\":\"ANSIGHT-AUTH-PROVE-V2\"}"
        val signature = clientKey.signP1363(proof)

        assertEquals(64, Base64.getDecoder().decode(signature).size)
        assertTrue(PairingV2Crypto.verifyP1363(keyPair.public, proof, signature))
    }

    @Test
    fun enrollmentAuthenticationConsumesChallengeAndReturnsVerifiedGrant() {
        val fixture = fixture()
        val init = ConnectInitV2(
            requestId = base64Url(ByteArray(16) { 1 }),
            configId = fixture.config.configId,
            appId = fixture.config.appId,
            clientNonce = base64Url(ByteArray(32) { 2 }),
        )
        val offer = signOffer(fixture, init)
        val challenge = challenge(init, offer)
        val keyPair = ecKeyPair()
        val clientKey = softwareClientKey(keyPair)
        val grant = signedGrant(fixture, clientKey.keyId)
        val channel = FakeAuthenticationChannel(
            listOf(
                org.json.JSONObject(challenge.canonicalJson()).toString(),
                org.json.JSONObject()
                    .put("type", PairingV2Constants.AuthOkType)
                    .put("ver", 2)
                    .put("sessionId", base64Url(ByteArray(16) { 7 }))
                    .put("grant", grant.toJson())
                    .toString(),
            ),
        )
        val result = PairingV2Authenticator(PairingClientKeyProvider { clientKey }) { now.toEpochMilli() }.authenticate(
            channel = channel,
            document = ParsedPairingDocument(fixture.config),
            init = init,
            offer = offer,
            requestedScopes = listOf("Read"),
            requestCritical = false,
        )

        assertEquals(clientKey.keyId, result.clientKeyId)
        assertEquals(grant.grantId, result.grant.grantId)
        val sent = org.json.JSONObject(channel.sent.single())
        assertEquals(PairingV2Constants.AuthEnrollType, sent.getString("type"))
        assertEquals(PairingV2Constants.EnrollmentProofAlgorithm, sent.getString("proofAlgorithm"))
        assertEquals(32, PairingV2Crypto.decodeBase64Url(sent.getString("proof")).size)
    }

    @Test
    fun reconnectAuthenticationSignsTheServerChallengeWithRememberedKey() {
        val fixture = fixture()
        val init = ConnectInitV2(
            requestId = base64Url(ByteArray(16) { 1 }),
            configId = fixture.config.configId,
            appId = fixture.config.appId,
            clientNonce = base64Url(ByteArray(32) { 2 }),
        )
        val offer = signOffer(fixture, init)
        val challenge = challenge(init, offer)
        val keyPair = ecKeyPair()
        val clientKey = softwareClientKey(keyPair)
        val grant = signedGrant(fixture, clientKey.keyId)
        val channel = FakeAuthenticationChannel(
            listOf(
                org.json.JSONObject(challenge.canonicalJson()).toString(),
                org.json.JSONObject()
                    .put("type", PairingV2Constants.AuthOkType)
                    .put("ver", 2)
                    .put("sessionId", base64Url(ByteArray(16) { 7 }))
                    .put("grant", grant.toJson())
                    .toString(),
            ),
        )
        PairingV2Authenticator(PairingClientKeyProvider { clientKey }) { now.toEpochMilli() }.authenticate(
            channel = channel,
            document = PairingRememberedProfileV2.create(ParsedPairingDocument(fixture.config), clientKey.keyId, grant).toParsedDocument(),
            init = init,
            offer = offer,
            requestedScopes = listOf("Read"),
            requestCritical = false,
        )

        val sent = org.json.JSONObject(channel.sent.single())
        assertEquals(PairingV2Constants.AuthProveType, sent.getString("type"))
        val proof = PairingV2ProofInputs.reconnect(init, offer, challenge, grant.grantId, clientKey.keyId)
        assertTrue(PairingV2Crypto.verifyP1363(keyPair.public, proof, sent.getString("signature")))
    }

    @Test
    fun redactionRemovesLegacyAndV2Secrets() {
        val redacted = PairingRedaction.redact(
            "wss://host/ws?token=bearer&grant=grant-value {\"secret\":\"ticket-secret\",\"proof\":\"proof-value\"}",
        )

        assertFalse(redacted.contains("bearer"))
        assertFalse(redacted.contains("grant-value"))
        assertFalse(redacted.contains("ticket-secret"))
        assertFalse(redacted.contains("proof-value"))
    }

    @Test
    fun authenticatedGrantRestrictsScopeAndCriticalTools() {
        val fixture = fixture()
        val grant = signedGrant(fixture, base64Url(ByteArray(32) { 4 }))
        val read = ToolDefinition("read", "Read", "Read", "test", ToolScope.Read, "read")
        val write = ToolDefinition("write", "Write", "Write", "test", ToolScope.Write, "write")
        val criticalRead = read.copy(security = ToolSecurity(ToolSecurityLevel.Critical))

        assertTrue(PairingV2Authorization.allows(grant, read))
        assertFalse(PairingV2Authorization.allows(grant, write))
        assertFalse(PairingV2Authorization.allows(grant, criticalRead))
    }

    @Test
    fun v1IsRejectedUnlessCompatibilityIsExplicit() {
        val config = PairingConfig(
            schema = PairingConfig.SchemaName,
            configId = "legacy",
            appId = "com.example.app",
            appName = "Legacy",
            issuedAt = now.toString(),
            expiresAt = now.plusSeconds(60).toString(),
            oneTimeToken = "secret",
            host = PairingHost(null, null, 45_123, "pub", "fingerprint"),
            challenge = PairingChallenge("ECDSA-P256", "challenge", true),
            signature = "signature",
        )

        val result = PairingSessionConnector(simulatorLocalHostAddressProvider = { "127.0.0.1" })
            .connect(ParsedPairingDocument(config), "Test")

        assertFalse(result.success)
        assertEquals(PairingFailureCodes.InsecureV1Disabled, result.failureCode)
    }

    @Test
    fun malformedV2PayloadsStillBlockDowngrade() {
        assertTrue(PairingV2DowngradePolicy.claimsV2("{\"schema\":\"ansight.pairing-config.v2\"}"))
        assertTrue(PairingV2DowngradePolicy.claimsV2("{\"schema\":\"ansight.pairing-config-document.v2\",\"config\":{}}"))
        assertTrue(PairingV2DowngradePolicy.claimsV2("apc2:not-valid-base64"))
        assertFalse(PairingV2DowngradePolicy.claimsV2("{\"schema\":\"ansight.pairing-config.v1\"}"))
    }

    @Test
    fun secureClientKeyFailsClosedWhenAndroidKeystoreP256IsUnavailable() {
        val error = assertThrows(IllegalArgumentException::class.java) {
            AndroidPairingClientKeyProvider.getOrCreate("host|app")
        }

        assertTrue(error.message.orEmpty().contains("API 23"))
    }

    private fun fixture(): V2Fixture {
        val hostKey = ecKeyPair()
        val tlsKey = ecKeyPair()
        val hostFingerprint = PairingV2Crypto.sha256Base64Url(hostKey.public.encoded)
        val pin = PairingTlsPinV2(
            tlsSpkiSha256 = PairingV2Crypto.sha256Base64Url(tlsKey.public.encoded),
            notBefore = now.minusSeconds(60).toString(),
            notAfter = now.plusSeconds(3_600).toString(),
        )
        val unsigned = PairingConfig(
            schema = PairingConfig.SecureSchemaName,
            configId = "cfg-v2",
            appId = "com.example.app",
            appName = "Example App",
            issuedAt = now.minusSeconds(60).toString(),
            expiresAt = now.plusSeconds(600).toString(),
            oneTimeToken = null,
            host = PairingHost(
                hostId = hostFingerprint,
                hostName = "Developer Mac",
                discoveryPort = 45_123,
                hostPubKey = Base64.getEncoder().encodeToString(hostKey.public.encoded),
                hostPubKeyFingerprint = hostFingerprint,
                tlsPins = listOf(pin),
            ),
            challenge = null,
            signature = "pending",
            minProtocolVersion = 2,
            allowedTransports = listOf("wss"),
            enrollment = PairingEnrollmentV2(
                ticketId = base64Url(ByteArray(16) { 8 }),
                secret = base64Url(ByteArray(32) { 9 }),
                expiresAt = now.plusSeconds(600).toString(),
                grantExpiresAt = now.plusSeconds(86_400).toString(),
                maxUses = 1,
                maxScopes = listOf("Read"),
                allowCritical = false,
            ),
            signatureAlgorithm = PairingV2Constants.SignatureAlgorithm,
        )
        return V2Fixture(unsigned.copy(signature = sign(hostKey, PairingCanonicalJson.serializePairingConfigV2ForSignature(unsigned))), hostKey)
    }

    private fun signOffer(fixture: V2Fixture, init: ConnectInitV2): ConnectOfferV2 {
        val unsigned = ConnectOfferV2(
            type = PairingV2Constants.ConnectOfferType,
            ver = 2,
            requestId = init.requestId,
            configId = init.configId,
            appId = init.appId,
            clientNonce = init.clientNonce,
            hostNonce = base64Url(ByteArray(32) { 3 }),
            hostId = fixture.config.host.hostId!!,
            selectedVersion = 2,
            selectedTransport = "wss",
            webSocketPort = 45_124,
            webSocketPath = "/ws/v2/offer",
            tlsSpkiSha256 = fixture.config.host.tlsPins.single().tlsSpkiSha256,
            expiresAt = now.plusSeconds(10).toString(),
            signatureAlgorithm = PairingV2Constants.SignatureAlgorithm,
            signature = "pending",
        )
        val signed = "ANSIGHT-CONNECT-OFFER-V2\n${init.canonicalJson()}\n${unsigned.canonicalJson()}"
        return unsigned.copy(signature = sign(fixture.hostKey, signed))
    }

    private fun challenge(init: ConnectInitV2, offer: ConnectOfferV2): AuthChallengeV2 = AuthChallengeV2(
        authSessionId = base64Url(ByteArray(16) { 4 }),
        requestId = init.requestId,
        configId = init.configId,
        appId = init.appId,
        clientNonce = init.clientNonce,
        hostNonce = offer.hostNonce,
        serverChallenge = base64Url(ByteArray(32) { 5 }),
        expiresAt = now.plusSeconds(20).toString(),
    )

    private fun signedGrant(fixture: V2Fixture, clientKeyId: String): PairingGrantV2 {
        val unsigned = PairingGrantV2(
            grantId = base64Url(ByteArray(16) { 6 }),
            hostId = fixture.config.host.hostId!!,
            configId = fixture.config.configId,
            appId = fixture.config.appId,
            clientKeyId = clientKeyId,
            allowedScopes = listOf("Read"),
            allowCritical = false,
            issuedAt = now.toString(),
            expiresAt = now.plusSeconds(3_600).toString(),
            signatureAlgorithm = PairingV2Constants.SignatureAlgorithm,
            signature = "pending",
        )
        return unsigned.copy(signature = sign(fixture.hostKey, PairingV2CanonicalJson.grant(unsigned)))
    }

    private fun sign(keyPair: KeyPair, content: String): String {
        val der = Signature.getInstance("SHA256withECDSA").run {
            initSign(keyPair.private)
            update(content.toByteArray(StandardCharsets.UTF_8))
            sign()
        }
        return Base64.getEncoder().encodeToString(PairingV2Crypto.derToP1363(der))
    }

    private fun softwareClientKey(keyPair: KeyPair): PairingClientKey = PairingClientKey(
        keyId = PairingV2Crypto.sha256Base64Url(keyPair.public.encoded),
        publicKeyBase64 = Base64.getEncoder().encodeToString(keyPair.public.encoded),
        persistent = true,
    ) { content ->
        Signature.getInstance("SHA256withECDSA").run {
            initSign(keyPair.private)
            update(content)
            sign()
        }
    }

    private fun ecKeyPair(): KeyPair = KeyPairGenerator.getInstance("EC").apply {
        initialize(ECGenParameterSpec("secp256r1"))
    }.generateKeyPair()

    private fun base64Url(bytes: ByteArray): String = Base64.getUrlEncoder().withoutPadding().encodeToString(bytes)

    private data class V2Fixture(val config: PairingConfig, val hostKey: KeyPair)

    private class FakeAuthenticationChannel(messages: List<String>) : PairingV2AuthenticationChannel {
        private val messages = ArrayDeque(messages)
        val sent = mutableListOf<String>()

        override fun awaitTextMessage(timeoutMilliseconds: Long): String? = messages.removeFirstOrNull()

        override fun sendText(text: String): OperationResult {
            sent += text
            return OperationResult.success("sent")
        }
    }
}
