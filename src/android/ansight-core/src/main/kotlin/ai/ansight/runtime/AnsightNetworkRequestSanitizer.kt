package ai.ansight.runtime

import android.util.Base64
import java.net.URLDecoder
import java.net.URLEncoder
import java.nio.ByteBuffer
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.util.Locale
import java.util.UUID

/** Mandatory privacy and size controls applied at the native transport boundary. */
object AnsightNetworkRequestSanitizer {
    const val RedactedValue = "<redacted>"

    private const val MaximumHeaderCount = 128
    private const val MaximumHeaderValueLength = 4096
    private const val MaximumErrorMessageLength = 4096
    private const val MaximumUrlLength = 16_384

    private val sensitiveHeaderNames = setOf(
        "authorization",
        "cookie",
        "proxy-authorization",
        "set-cookie",
        "x-api-key",
        "x-auth-token",
    )
    private val sensitiveQueryNames = setOf(
        "access_token",
        "accesskey",
        "access_key",
        "api_key",
        "apikey",
        "auth",
        "authorization",
        "client_secret",
        "code",
        "credential",
        "credentials",
        "id_token",
        "jwt",
        "key",
        "password",
        "passwd",
        "refresh_token",
        "sas",
        "sastoken",
        "secret",
        "secret_key",
        "security_token",
        "session_token",
        "sig",
        "signature",
        "token",
    )
    private val azureSasFingerprintNames = setOf("se", "skoid", "sp", "sr", "srt", "ss", "sv")
    private val azureSasQueryNames = setOf(
        "epk", "erk", "rscc", "rscd", "rsce", "rscl", "rsct", "saoid", "scid", "se",
        "sig", "si", "sip", "ske", "skoid", "sks", "skt", "sktid", "skv", "snapshot",
        "sp", "spk", "spr", "sr", "srk", "srt", "ss", "st", "suoid", "tn", "versionid", "sv",
    )
    private val sensitiveAssignmentPattern = Regex(
        "(?i)(access_token|accesskey|access_key|api_key|apikey|auth|authorization|client_secret|code|credential|credentials|id_token|jwt|key|password|passwd|refresh_token|sas|sastoken|secret|secret_key|security_token|session_token|sig|signature|token)([\\\"']?\\s*[:=]\\s*[\\\"']?)([^&\\s,;}\\\"']+)",
    )
    private val absoluteUrlPattern = Regex("(?i)https?://[^\\s\\\"'<>]+")
    private val absoluteUserInfoPattern = Regex("(?i)^(https?://)[^/@]+@")

    fun sanitize(request: AnsightNetworkRequest): AnsightNetworkRequest {
        val startedAtUtc = normalizeTimestamp(request.startedAtUtc, AnsightClock.isoNow())
        val normalizedCompletion = normalizeTimestamp(request.completedAtUtc, startedAtUtc)
        val completedAtUtc = if (Instant.parse(normalizedCompletion).isBefore(Instant.parse(startedAtUtc))) {
            startedAtUtc
        } else {
            normalizedCompletion
        }
        val duration = request.durationMilliseconds.takeIf(Double::isFinite)?.coerceAtLeast(0.0) ?: 0.0
        return request.copy(
            schema = AnsightNetworkRequest.SchemaName,
            id = normalizeRequired(request.id, UUID.randomUUID().toString().replace("-", ""), 128),
            source = normalizeRequired(request.source, "unknown", 128),
            startedAtUtc = startedAtUtc,
            completedAtUtc = completedAtUtc,
            durationMilliseconds = duration,
            method = normalizeRequired(request.method, "GET", 32).uppercase(Locale.US),
            url = sanitizeUrl(request.url),
            protocol = normalizeOptional(request.protocol, 64),
            requestHeaders = sanitizeHeaders(request.requestHeaders),
            requestBodySizeBytes = normalizeSize(request.requestBodySizeBytes),
            requestBody = sanitizeBody(request.requestBody),
            statusCode = request.statusCode?.takeIf { it in 100..999 },
            reasonPhrase = normalizeOptional(request.reasonPhrase, 512),
            responseHeaders = sanitizeHeaders(request.responseHeaders),
            responseBodySizeBytes = normalizeSize(request.responseBodySizeBytes),
            responseBody = sanitizeBody(request.responseBody),
            errorType = normalizeOptional(request.errorType, 512),
            errorMessage = sanitizeErrorMessage(request.errorMessage),
        )
    }

    private fun sanitizeHeaders(headers: List<AnsightNetworkHeader>): List<AnsightNetworkHeader> =
        headers.asSequence()
            .filter { it.name.isNotBlank() }
            .take(MaximumHeaderCount)
            .map { header ->
                val name = normalizeRequired(header.name, "Header", 256)
                AnsightNetworkHeader(
                    name = name,
                    value = if (isSensitiveHeader(name)) {
                        RedactedValue
                    } else {
                        normalizeRequired(header.value, "", MaximumHeaderValueLength)
                    },
                )
            }
            .toList()

    private fun sanitizeUrl(value: String?): String {
        val normalized = normalizeRequired(value, "<unknown>", MaximumUrlLength)
        val withoutUserInfo = absoluteUserInfoPattern.replace(normalized, "\$1$RedactedValue@")
        val queryIndex = withoutUserInfo.indexOf('?')
        if (queryIndex < 0) return truncate(withoutUserInfo, MaximumUrlLength)

        val fragmentIndex = withoutUserInfo.indexOf('#', queryIndex)
        val queryEnd = if (fragmentIndex < 0) withoutUserInfo.length else fragmentIndex
        val query = withoutUserInfo.substring(queryIndex + 1, queryEnd)
        val fragment = if (fragmentIndex < 0) "" else withoutUserInfo.substring(fragmentIndex)
        return truncate(
            withoutUserInfo.substring(0, queryIndex) + "?" + sanitizeQuery(query) + fragment,
            MaximumUrlLength,
        )
    }

    private fun sanitizeQuery(query: String): String {
        val pairs = query.split('&')
        val names = pairs.map(::decodeQueryName).map { it.lowercase(Locale.US) }.toSet()
        val hasAzureSas = "sig" in names && azureSasFingerprintNames.any { it in names }
        val hasAwsSignature = "x-amz-signature" in names
        val hasGoogleSignature = "x-goog-signature" in names
        val hasCloudFrontSignature = "signature" in names &&
            setOf("key-pair-id", "policy", "expires").any { it in names }
        val hasLegacyGoogleSignature = "signature" in names && "googleaccessid" in names
        val hasAlibabaSignature = ("signature" in names && "ossaccesskeyid" in names) ||
            "x-oss-signature" in names
        return pairs.joinToString("&") { pair ->
        val equalsIndex = pair.indexOf('=')
        val encodedName = if (equalsIndex < 0) pair else pair.substring(0, equalsIndex)
        val lowered = decodeQueryName(pair).lowercase(Locale.US)
        val providerSensitive = (hasAzureSas && lowered in azureSasQueryNames) ||
            (hasAwsSignature && lowered.startsWith("x-amz-")) ||
            (hasGoogleSignature && lowered.startsWith("x-goog-")) ||
            (hasCloudFrontSignature && lowered in setOf("signature", "key-pair-id", "policy", "expires", "hash-algorithm")) ||
            (hasLegacyGoogleSignature && lowered in setOf("signature", "googleaccessid", "expires")) ||
            (hasAlibabaSignature && (lowered.startsWith("x-oss-") ||
                lowered in setOf("signature", "ossaccesskeyid", "security-token")))
        if (!providerSensitive && lowered !in sensitiveQueryNames) {
            pair
        } else {
            "$encodedName=${URLEncoder.encode(RedactedValue, StandardCharsets.UTF_8.name())}"
        }
        }
    }

    private fun decodeQueryName(pair: String): String {
        val equalsIndex = pair.indexOf('=')
        val encodedName = if (equalsIndex < 0) pair else pair.substring(0, equalsIndex)
        return runCatching {
            URLDecoder.decode(encodedName.replace("+", " "), StandardCharsets.UTF_8.name())
        }.getOrDefault(encodedName)
    }

    private fun sanitizeBody(body: AnsightNetworkBody?): AnsightNetworkBody? {
        body ?: return null
        val encoding = body.encoding.trim().lowercase(Locale.US)
        val decoded = runCatching {
            when (encoding) {
                "utf8" -> sanitizeSensitiveText(body.data).toByteArray(StandardCharsets.UTF_8)
                "base64" -> Base64.decode(body.data, Base64.DEFAULT)
                else -> return null
            }
        }.getOrNull() ?: return null
        var captured = decoded
        if (encoding == "utf8") captured = completeUtf8(captured)
        val totalBytes = normalizeSize(body.totalBytes)
        return AnsightNetworkBody(
            contentType = normalizeOptional(body.contentType, 512),
            encoding = encoding,
            data = if (encoding == "base64") {
                Base64.encodeToString(captured, Base64.NO_WRAP)
            } else {
                captured.toString(StandardCharsets.UTF_8)
            },
            capturedBytes = captured.size.toLong(),
            totalBytes = totalBytes,
            truncated = body.truncated || decoded.size > captured.size ||
                (totalBytes != null && totalBytes > captured.size),
        )
    }

    private fun completeUtf8(bytes: ByteArray): ByteArray {
        var length = bytes.size
        while (length > 0) {
            val valid = runCatching {
                StandardCharsets.UTF_8.newDecoder()
                    .onMalformedInput(CodingErrorAction.REPORT)
                    .onUnmappableCharacter(CodingErrorAction.REPORT)
                    .decode(ByteBuffer.wrap(bytes, 0, length))
            }.isSuccess
            if (valid) return if (length == bytes.size) bytes else bytes.copyOf(length)
            length--
        }
        return byteArrayOf()
    }

    private fun sanitizeSensitiveText(value: String): String {
        val assignments = sensitiveAssignmentPattern.replace(value) { match ->
            "${match.groupValues[1]}${match.groupValues[2]}$RedactedValue"
        }
        return absoluteUrlPattern.replace(assignments) { match -> sanitizeUrl(match.value) }
    }

    private fun isSensitiveHeader(name: String): Boolean {
        val lowered = name.lowercase(Locale.US)
        if (lowered in sensitiveHeaderNames) return true
        val compact = lowered.replace("-", "")
        return compact.contains("token") || compact.contains("secret") || compact.contains("apikey")
    }

    private fun sanitizeErrorMessage(value: String?): String? {
        val normalized = normalizeOptional(value, MaximumErrorMessageLength) ?: return null
        val assignmentsRedacted = sensitiveAssignmentPattern.replace(normalized) { match ->
            "${match.groupValues[1]}${match.groupValues[2]}$RedactedValue"
        }
        val urlsRedacted = absoluteUrlPattern.replace(assignmentsRedacted) { match ->
            sanitizeUrl(match.value)
        }
        return truncate(urlsRedacted, MaximumErrorMessageLength)
    }

    private fun normalizeTimestamp(value: String?, fallback: String): String =
        normalizeOptional(value, 128)?.takeIf { runCatching { Instant.parse(it) }.isSuccess } ?: fallback

    private fun normalizeSize(value: Long?): Long? = value?.takeIf { it >= 0 }

    private fun normalizeRequired(value: String?, fallback: String, maximumLength: Int): String =
        truncate(value?.trim()?.takeIf(String::isNotEmpty) ?: fallback, maximumLength)

    private fun normalizeOptional(value: String?, maximumLength: Int): String? =
        value?.trim()?.takeIf(String::isNotEmpty)?.let { truncate(it, maximumLength) }

    private fun truncate(value: String, maximumLength: Int): String =
        if (value.length <= maximumLength) value else value.take(maximumLength) + "…"
}
