package ai.ansight.runtime

import org.json.JSONArray
import org.json.JSONObject

internal data class ConnectInitV2(
    val requestId: String,
    val configId: String,
    val appId: String,
    val clientNonce: String,
) {
    fun toJson(): JSONObject = JSONObject(canonicalJson())

    fun canonicalJson(): String = listOf(
        PairingCanonicalJson.stringField("type", PairingV2Constants.ConnectInitType),
        "\"ver\":2",
        PairingCanonicalJson.stringField("requestId", requestId),
        PairingCanonicalJson.stringField("configId", configId),
        PairingCanonicalJson.stringField("appId", appId),
        PairingCanonicalJson.stringField("clientNonce", clientNonce),
        "\"supportedVersions\":[2]",
        "\"supportedTransports\":[\"wss\"]",
    ).joinToString(prefix = "{", postfix = "}", separator = ",")
}

internal data class ConnectOfferV2(
    val type: String,
    val ver: Int,
    val requestId: String,
    val configId: String,
    val appId: String,
    val clientNonce: String,
    val hostNonce: String,
    val hostId: String,
    val selectedVersion: Int,
    val selectedTransport: String,
    val webSocketPort: Int,
    val webSocketPath: String,
    val tlsSpkiSha256: String,
    val expiresAt: String,
    val signatureAlgorithm: String,
    val signature: String,
) {
    fun canonicalJson(): String = listOf(
        PairingCanonicalJson.stringField("type", type),
        "\"ver\":$ver",
        PairingCanonicalJson.stringField("requestId", requestId),
        PairingCanonicalJson.stringField("configId", configId),
        PairingCanonicalJson.stringField("appId", appId),
        PairingCanonicalJson.stringField("clientNonce", clientNonce),
        PairingCanonicalJson.stringField("hostNonce", hostNonce),
        PairingCanonicalJson.stringField("hostId", hostId),
        "\"selectedVersion\":$selectedVersion",
        PairingCanonicalJson.stringField("selectedTransport", selectedTransport),
        "\"webSocketPort\":$webSocketPort",
        PairingCanonicalJson.stringField("webSocketPath", webSocketPath),
        PairingCanonicalJson.stringField("tlsSpkiSha256", tlsSpkiSha256),
        PairingCanonicalJson.stringField("expiresAt", expiresAt),
        PairingCanonicalJson.stringField("signatureAlgorithm", signatureAlgorithm),
    ).joinToString(prefix = "{", postfix = "}", separator = ",")

    companion object {
        fun fromJson(json: JSONObject): ConnectOfferV2 = ConnectOfferV2(
            type = json.requiredString("type"),
            ver = json.optInt("ver", 0),
            requestId = json.requiredString("requestId"),
            configId = json.requiredString("configId"),
            appId = json.requiredString("appId"),
            clientNonce = json.requiredString("clientNonce"),
            hostNonce = json.requiredString("hostNonce"),
            hostId = json.requiredString("hostId"),
            selectedVersion = json.optInt("selectedVersion", 0),
            selectedTransport = json.requiredString("selectedTransport"),
            webSocketPort = json.optInt("webSocketPort", 0),
            webSocketPath = json.requiredString("webSocketPath"),
            tlsSpkiSha256 = json.requiredString("tlsSpkiSha256"),
            expiresAt = json.requiredString("expiresAt"),
            signatureAlgorithm = json.requiredString("signatureAlgorithm"),
            signature = json.requiredString("signature"),
        )
    }
}

internal data class AuthChallengeV2(
    val authSessionId: String,
    val requestId: String,
    val configId: String,
    val appId: String,
    val clientNonce: String,
    val hostNonce: String,
    val serverChallenge: String,
    val expiresAt: String,
) {
    fun canonicalJson(): String = listOf(
        PairingCanonicalJson.stringField("type", PairingV2Constants.AuthChallengeType),
        "\"ver\":2",
        PairingCanonicalJson.stringField("authSessionId", authSessionId),
        PairingCanonicalJson.stringField("requestId", requestId),
        PairingCanonicalJson.stringField("configId", configId),
        PairingCanonicalJson.stringField("appId", appId),
        PairingCanonicalJson.stringField("clientNonce", clientNonce),
        PairingCanonicalJson.stringField("hostNonce", hostNonce),
        PairingCanonicalJson.stringField("serverChallenge", serverChallenge),
        PairingCanonicalJson.stringField("expiresAt", expiresAt),
    ).joinToString(prefix = "{", postfix = "}", separator = ",")

    companion object {
        fun fromJson(json: JSONObject): AuthChallengeV2 {
            require(json.requiredString("type") == PairingV2Constants.AuthChallengeType && json.optInt("ver", 0) == 2) {
                "Expected a protocol v2 authentication challenge."
            }
            return AuthChallengeV2(
                authSessionId = json.requiredString("authSessionId"),
                requestId = json.requiredString("requestId"),
                configId = json.requiredString("configId"),
                appId = json.requiredString("appId"),
                clientNonce = json.requiredString("clientNonce"),
                hostNonce = json.requiredString("hostNonce"),
                serverChallenge = json.requiredString("serverChallenge"),
                expiresAt = json.requiredString("expiresAt"),
            )
        }
    }
}

internal data class AuthEnrollV2(
    val authSessionId: String,
    val ticketId: String,
    val clientKeyId: String,
    val clientPublicKey: String,
    val requestedScopes: List<String>,
    val requestCritical: Boolean,
    val proof: String,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("type", PairingV2Constants.AuthEnrollType)
        .put("ver", 2)
        .put("authSessionId", authSessionId)
        .put("ticketId", ticketId)
        .put("clientKeyId", clientKeyId)
        .put("clientPublicKey", clientPublicKey)
        .put("requestedScopes", JSONArray(PairingV2Scopes.normalize(requestedScopes)))
        .put("requestCritical", requestCritical)
        .put("proofAlgorithm", PairingV2Constants.EnrollmentProofAlgorithm)
        .put("proof", proof)
}

internal data class AuthProveV2(
    val authSessionId: String,
    val grantId: String,
    val clientKeyId: String,
    val signature: String,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("type", PairingV2Constants.AuthProveType)
        .put("ver", 2)
        .put("authSessionId", authSessionId)
        .put("grantId", grantId)
        .put("clientKeyId", clientKeyId)
        .put("signatureAlgorithm", PairingV2Constants.SignatureAlgorithm)
        .put("signature", signature)
}

internal data class AuthOkV2(
    val sessionId: String,
    val grant: PairingGrantV2,
) {
    companion object {
        fun fromJson(json: JSONObject): AuthOkV2 {
            require(json.requiredString("type") == PairingV2Constants.AuthOkType && json.optInt("ver", 0) == 2) {
                "Expected a protocol v2 authentication result."
            }
            return AuthOkV2(
                sessionId = json.requiredString("sessionId"),
                grant = PairingGrantV2.fromJson(json.getJSONObject("grant")),
            )
        }
    }
}

internal data class AuthErrorV2(
    val code: String,
    val message: String,
    val retryable: Boolean,
) {
    companion object {
        fun fromJson(json: JSONObject): AuthErrorV2 = AuthErrorV2(
            code = json.requiredString("code"),
            message = json.requiredString("message"),
            retryable = json.optBoolean("retryable", false),
        )
    }
}

internal object PairingV2ProofInputs {
    fun enrollment(
        configSignatureSha256: String,
        init: ConnectInitV2,
        offer: ConnectOfferV2,
        challenge: AuthChallengeV2,
        enrollment: PairingEnrollmentV2,
        clientKeyId: String,
        clientPublicKey: String,
        requestedScopes: List<String>,
        requestCritical: Boolean,
    ): String = listOf(
        PairingCanonicalJson.stringField("context", "ANSIGHT-AUTH-ENROLL-V2"),
        PairingCanonicalJson.stringField("configSignatureSha256", configSignatureSha256),
        PairingCanonicalJson.stringField("requestId", init.requestId),
        PairingCanonicalJson.stringField("clientNonce", init.clientNonce),
        PairingCanonicalJson.stringField("hostNonce", offer.hostNonce),
        PairingCanonicalJson.stringField("tlsSpkiSha256", offer.tlsSpkiSha256),
        PairingCanonicalJson.stringField("authSessionId", challenge.authSessionId),
        PairingCanonicalJson.stringField("serverChallenge", challenge.serverChallenge),
        PairingCanonicalJson.stringField("ticketId", enrollment.ticketId),
        PairingCanonicalJson.stringField("clientKeyId", clientKeyId),
        PairingCanonicalJson.stringField("clientPublicKey", clientPublicKey),
        "\"requestedScopes\":${PairingCanonicalJson.serializeScopes(requestedScopes)}",
        "\"requestCritical\":$requestCritical",
    ).joinToString(prefix = "{", postfix = "}", separator = ",")

    fun reconnect(
        init: ConnectInitV2,
        offer: ConnectOfferV2,
        challenge: AuthChallengeV2,
        grantId: String,
        clientKeyId: String,
    ): String = listOf(
        PairingCanonicalJson.stringField("context", "ANSIGHT-AUTH-PROVE-V2"),
        PairingCanonicalJson.stringField("requestId", init.requestId),
        PairingCanonicalJson.stringField("clientNonce", init.clientNonce),
        PairingCanonicalJson.stringField("hostNonce", offer.hostNonce),
        PairingCanonicalJson.stringField("tlsSpkiSha256", offer.tlsSpkiSha256),
        PairingCanonicalJson.stringField("authSessionId", challenge.authSessionId),
        PairingCanonicalJson.stringField("serverChallenge", challenge.serverChallenge),
        PairingCanonicalJson.stringField("grantId", grantId),
        PairingCanonicalJson.stringField("clientKeyId", clientKeyId),
    ).joinToString(prefix = "{", postfix = "}", separator = ",")
}
