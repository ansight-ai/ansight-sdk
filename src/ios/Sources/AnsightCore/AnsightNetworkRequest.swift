import Foundation

/// One captured HTTP header after sensitive-value redaction.
public struct AnsightNetworkHeader: Codable, Equatable, Sendable {
    public let name: String
    public let value: String

    public init(name: String, value: String) {
        self.name = name
        self.value = value
    }
}

public struct AnsightNetworkBody: Codable, Equatable, Sendable {
    public let contentType: String?
    public let encoding: String
    public let data: String
    public let capturedBytes: Int64
    public let totalBytes: Int64?
    public let truncated: Bool

    public init(
        contentType: String? = nil,
        encoding: String,
        data: String,
        capturedBytes: Int64,
        totalBytes: Int64? = nil,
        truncated: Bool
    ) {
        self.contentType = contentType
        self.encoding = encoding
        self.data = data
        self.capturedBytes = capturedBytes
        self.totalBytes = totalBytes
        self.truncated = truncated
    }
}

/// Captured data for one completed HTTP request.
/// Text bodies are included by default by capture integrations and bounded before transport.
public struct AnsightNetworkRequest: Codable, Equatable, Sendable {
    public static let schemaName = "ansight.network-request.v1"

    public let schema: String
    public let id: String
    public let source: String
    public let startedAtUtc: String
    public let completedAtUtc: String
    public let durationMilliseconds: Double
    public let method: String
    public let url: String
    public let redactSensitiveData: Bool
    public let `protocol`: String?
    public let requestHeaders: [AnsightNetworkHeader]
    public let requestBodySizeBytes: Int64?
    public let requestBody: AnsightNetworkBody?
    public let statusCode: Int?
    public let reasonPhrase: String?
    public let responseHeaders: [AnsightNetworkHeader]
    public let responseBodySizeBytes: Int64?
    public let responseBody: AnsightNetworkBody?
    public let errorType: String?
    public let errorMessage: String?

    public init(
        schema: String = AnsightNetworkRequest.schemaName,
        id: String,
        source: String,
        startedAtUtc: String,
        completedAtUtc: String,
        durationMilliseconds: Double,
        method: String,
        url: String,
        redactSensitiveData: Bool = true,
        protocol: String? = nil,
        requestHeaders: [AnsightNetworkHeader] = [],
        requestBodySizeBytes: Int64? = nil,
        requestBody: AnsightNetworkBody? = nil,
        statusCode: Int? = nil,
        reasonPhrase: String? = nil,
        responseHeaders: [AnsightNetworkHeader] = [],
        responseBodySizeBytes: Int64? = nil,
        responseBody: AnsightNetworkBody? = nil,
        errorType: String? = nil,
        errorMessage: String? = nil
    ) {
        self.schema = schema
        self.id = id
        self.source = source
        self.startedAtUtc = startedAtUtc
        self.completedAtUtc = completedAtUtc
        self.durationMilliseconds = durationMilliseconds
        self.method = method
        self.url = url
        self.redactSensitiveData = redactSensitiveData
        self.protocol = `protocol`
        self.requestHeaders = requestHeaders
        self.requestBodySizeBytes = requestBodySizeBytes
        self.requestBody = requestBody
        self.statusCode = statusCode
        self.reasonPhrase = reasonPhrase
        self.responseHeaders = responseHeaders
        self.responseBodySizeBytes = responseBodySizeBytes
        self.responseBody = responseBody
        self.errorType = errorType
        self.errorMessage = errorMessage
    }

    func withRedactSensitiveData(_ enabled: Bool) -> AnsightNetworkRequest {
        AnsightNetworkRequest(
            schema: schema,
            id: id,
            source: source,
            startedAtUtc: startedAtUtc,
            completedAtUtc: completedAtUtc,
            durationMilliseconds: durationMilliseconds,
            method: method,
            url: url,
            redactSensitiveData: enabled,
            protocol: `protocol`,
            requestHeaders: requestHeaders,
            requestBodySizeBytes: requestBodySizeBytes,
            requestBody: requestBody,
            statusCode: statusCode,
            reasonPhrase: reasonPhrase,
            responseHeaders: responseHeaders,
            responseBodySizeBytes: responseBodySizeBytes,
            responseBody: responseBody,
            errorType: errorType,
            errorMessage: errorMessage
        )
    }
}

/// Mandatory privacy and size controls applied at the native transport boundary.
public enum AnsightNetworkRequestSanitizer {
    public static let redactedValue = "<redacted>"

    private static let maximumHeaderCount = 128
    private static let maximumHeaderValueLength = 4_096
    private static let maximumErrorMessageLength = 4_096
    private static let maximumUrlLength = 16_384
    private static let sensitiveHeaderNames: Set<String> = [
        "authorization",
        "cookie",
        "proxy-authorization",
        "set-cookie",
        "x-api-key",
        "x-auth-token",
    ]
    private static let sensitiveQueryNames: Set<String> = [
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
    ]
    private static let azureSasFingerprintNames: Set<String> = [
        "se", "skoid", "sp", "sr", "srt", "ss", "sv",
    ]
    private static let azureSasQueryNames: Set<String> = [
        "epk", "erk", "rscc", "rscd", "rsce", "rscl", "rsct", "saoid", "scid", "se",
        "sig", "si", "sip", "ske", "skoid", "sks", "skt", "sktid", "skv", "snapshot",
        "sp", "spk", "spr", "sr", "srk", "srt", "ss", "st", "suoid", "tn", "versionid", "sv",
    ]
    private static let sensitiveAssignmentPattern = try! NSRegularExpression(
        pattern: #"(?i)(access_token|accesskey|access_key|api_key|apikey|auth|authorization|client_secret|code|credential|credentials|id_token|jwt|key|password|passwd|refresh_token|sas|sastoken|secret|secret_key|security_token|session_token|sig|signature|token)([\"']?\s*[:=]\s*[\"']?)([^&\s,;}\"']+)"#
    )
    private static let absoluteUrlPattern = try! NSRegularExpression(
        pattern: #"(?i)https?://[^\s\"'<>]+"#
    )
    private static let absoluteUserInfoPattern = try! NSRegularExpression(
        pattern: #"(?i)^(https?://)[^/@]+@"#
    )

    public static func sanitize(_ request: AnsightNetworkRequest) -> AnsightNetworkRequest {
        let startedAtUtc = normalizeTimestamp(request.startedAtUtc, fallback: AnsightClock.isoNow())
        let normalizedCompletion = normalizeTimestamp(request.completedAtUtc, fallback: startedAtUtc)
        let formatter = ISO8601DateFormatter()
        let completedAtUtc = if let started = formatter.date(from: startedAtUtc),
                                let completed = formatter.date(from: normalizedCompletion),
                                completed < started
        {
            startedAtUtc
        } else {
            normalizedCompletion
        }
        return AnsightNetworkRequest(
            id: normalizeRequired(
                request.id,
                fallback: UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased(),
                maximumLength: 128
            ),
            source: normalizeRequired(request.source, fallback: "unknown", maximumLength: 128),
            startedAtUtc: startedAtUtc,
            completedAtUtc: completedAtUtc,
            durationMilliseconds: request.durationMilliseconds.isFinite
                ? max(0, request.durationMilliseconds)
                : 0,
            method: normalizeRequired(request.method, fallback: "GET", maximumLength: 32).uppercased(),
            url: sanitizeUrl(request.url),
            redactSensitiveData: true,
            protocol: normalizeOptional(request.protocol, maximumLength: 64),
            requestHeaders: sanitizeHeaders(request.requestHeaders),
            requestBodySizeBytes: normalizeSize(request.requestBodySizeBytes),
            requestBody: sanitizeBody(request.requestBody),
            statusCode: request.statusCode.flatMap { (100...999).contains($0) ? $0 : nil },
            reasonPhrase: normalizeOptional(request.reasonPhrase, maximumLength: 512),
            responseHeaders: sanitizeHeaders(request.responseHeaders),
            responseBodySizeBytes: normalizeSize(request.responseBodySizeBytes),
            responseBody: sanitizeBody(request.responseBody),
            errorType: normalizeOptional(request.errorType, maximumLength: 512),
            errorMessage: sanitizeErrorMessage(request.errorMessage)
        )
    }

    private static func sanitizeHeaders(_ headers: [AnsightNetworkHeader]) -> [AnsightNetworkHeader] {
        headers.lazy
            .filter { !$0.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
            .prefix(maximumHeaderCount)
            .map { header in
                let name = normalizeRequired(header.name, fallback: "Header", maximumLength: 256)
                return AnsightNetworkHeader(
                    name: name,
                    value: isSensitiveHeader(name)
                        ? redactedValue
                        : normalizeRequired(header.value, fallback: "", maximumLength: maximumHeaderValueLength)
                )
            }
    }

    private static func sanitizeUrl(_ value: String?) -> String {
        let normalized = normalizeRequired(value, fallback: "<unknown>", maximumLength: maximumUrlLength)
        let withoutUserInfo = replace(
            absoluteUserInfoPattern,
            in: normalized,
            template: "$1\(redactedValue)@"
        )
        guard let queryIndex = withoutUserInfo.firstIndex(of: "?") else {
            return truncate(withoutUserInfo, maximumLength: maximumUrlLength)
        }

        let fragmentIndex = withoutUserInfo[queryIndex...].firstIndex(of: "#")
        let queryStart = withoutUserInfo.index(after: queryIndex)
        let queryEnd = fragmentIndex ?? withoutUserInfo.endIndex
        let query = String(withoutUserInfo[queryStart..<queryEnd])
        let fragment = fragmentIndex.map { String(withoutUserInfo[$0...]) } ?? ""
        let result = String(withoutUserInfo[..<queryIndex]) + "?" + sanitizeQuery(query) + fragment
        return truncate(result, maximumLength: maximumUrlLength)
    }

    private static func sanitizeQuery(_ query: String) -> String {
        let pairs = query.split(separator: "&", omittingEmptySubsequences: false).map(String.init)
        let names = Set(pairs.map(decodeQueryName).map { $0.lowercased() })
        let hasAzureSas = names.contains("sig") && !names.isDisjoint(with: azureSasFingerprintNames)
        let hasAwsSignature = names.contains("x-amz-signature")
        let hasGoogleSignature = names.contains("x-goog-signature")
        let hasCloudFrontSignature = names.contains("signature") &&
            !names.isDisjoint(with: ["key-pair-id", "policy", "expires"])
        let hasLegacyGoogleSignature = names.contains("signature") && names.contains("googleaccessid")
        let hasAlibabaSignature = (names.contains("signature") && names.contains("ossaccesskeyid")) ||
            names.contains("x-oss-signature")
        return pairs.map { pair in
            let equalsIndex = pair.firstIndex(of: "=")
            let encodedName = equalsIndex.map { String(pair[..<$0]) } ?? pair
            let lowered = decodeQueryName(pair).lowercased()
            let providerSensitive = (hasAzureSas && azureSasQueryNames.contains(lowered)) ||
                (hasAwsSignature && lowered.hasPrefix("x-amz-")) ||
                (hasGoogleSignature && lowered.hasPrefix("x-goog-")) ||
                (hasCloudFrontSignature && ["signature", "key-pair-id", "policy", "expires", "hash-algorithm"].contains(lowered)) ||
                (hasLegacyGoogleSignature && ["signature", "googleaccessid", "expires"].contains(lowered)) ||
                (hasAlibabaSignature && (lowered.hasPrefix("x-oss-") ||
                    ["signature", "ossaccesskeyid", "security-token"].contains(lowered)))
            guard providerSensitive || sensitiveQueryNames.contains(lowered) else {
                return pair
            }
            let encodedRedacted = redactedValue.addingPercentEncoding(
                withAllowedCharacters: .urlQueryAllowed
            ) ?? redactedValue
            return "\(encodedName)=\(encodedRedacted)"
        }.joined(separator: "&")
    }

    private static func decodeQueryName(_ pair: String) -> String {
        let equalsIndex = pair.firstIndex(of: "=")
        let encodedName = equalsIndex.map { String(pair[..<$0]) } ?? pair
        return encodedName
            .replacingOccurrences(of: "+", with: " ")
            .removingPercentEncoding ?? encodedName
    }

    private static func sanitizeBody(_ body: AnsightNetworkBody?) -> AnsightNetworkBody? {
        guard let body else { return nil }
        let encoding = body.encoding.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let decoded: Data
        if encoding == "utf8" {
            decoded = Data(sanitizeSensitiveText(body.data).utf8)
        } else if encoding == "base64", let value = Data(base64Encoded: body.data) {
            decoded = value
        } else {
            return nil
        }
        var captured = decoded
        if encoding == "utf8" {
            captured = completeUtf8(captured)
        }
        let totalBytes = normalizeSize(body.totalBytes)
        return AnsightNetworkBody(
            contentType: normalizeOptional(body.contentType, maximumLength: 512),
            encoding: encoding,
            data: encoding == "base64"
                ? captured.base64EncodedString()
                : String(data: captured, encoding: .utf8) ?? "",
            capturedBytes: Int64(captured.count),
            totalBytes: totalBytes,
            truncated: body.truncated || decoded.count > captured.count ||
                (totalBytes.map { $0 > Int64(captured.count) } ?? false)
        )
    }

    private static func completeUtf8(_ data: Data) -> Data {
        var length = data.count
        while length > 0 {
            let candidate = Data(data.prefix(length))
            if String(data: candidate, encoding: .utf8) != nil {
                return candidate
            }
            length -= 1
        }
        return Data()
    }

    private static func sanitizeSensitiveText(_ value: String) -> String {
        let assignments = replace(
            sensitiveAssignmentPattern,
            in: value,
            template: "$1$2\(redactedValue)"
        )
        let range = NSRange(assignments.startIndex..., in: assignments)
        var result = assignments
        for match in absoluteUrlPattern.matches(in: assignments, range: range).reversed() {
            guard let swiftRange = Range(match.range, in: result) else { continue }
            result.replaceSubrange(swiftRange, with: sanitizeUrl(String(result[swiftRange])))
        }
        return result
    }

    private static func isSensitiveHeader(_ name: String) -> Bool {
        let lowered = name.lowercased()
        if sensitiveHeaderNames.contains(lowered) {
            return true
        }
        let compact = lowered.replacingOccurrences(of: "-", with: "")
        return compact.contains("token") || compact.contains("secret") || compact.contains("apikey")
    }

    private static func sanitizeErrorMessage(_ value: String?) -> String? {
        guard let normalized = normalizeOptional(value, maximumLength: maximumErrorMessageLength) else {
            return nil
        }
        let assignmentsRedacted = replace(
            sensitiveAssignmentPattern,
            in: normalized,
            template: "$1$2\(redactedValue)"
        )
        let range = NSRange(assignmentsRedacted.startIndex..., in: assignmentsRedacted)
        var result = assignmentsRedacted
        for match in absoluteUrlPattern.matches(in: assignmentsRedacted, range: range).reversed() {
            guard let swiftRange = Range(match.range, in: result) else {
                continue
            }
            result.replaceSubrange(swiftRange, with: sanitizeUrl(String(result[swiftRange])))
        }
        return truncate(result, maximumLength: maximumErrorMessageLength)
    }

    private static func replace(
        _ expression: NSRegularExpression,
        in value: String,
        template: String
    ) -> String {
        expression.stringByReplacingMatches(
            in: value,
            range: NSRange(value.startIndex..., in: value),
            withTemplate: template
        )
    }

    private static func normalizeTimestamp(_ value: String?, fallback: String) -> String {
        guard let normalized = normalizeOptional(value, maximumLength: 128),
              ISO8601DateFormatter().date(from: normalized) != nil
        else {
            return fallback
        }
        return normalized
    }

    private static func normalizeSize(_ value: Int64?) -> Int64? {
        value.flatMap { $0 >= 0 ? $0 : nil }
    }

    private static func normalizeRequired(
        _ value: String?,
        fallback: String,
        maximumLength: Int
    ) -> String {
        let normalized = value?.trimmingCharacters(in: .whitespacesAndNewlines)
        return truncate(
            normalized.flatMap { $0.isEmpty ? nil : $0 } ?? fallback,
            maximumLength: maximumLength
        )
    }

    private static func normalizeOptional(_ value: String?, maximumLength: Int) -> String? {
        guard let normalized = value?.trimmingCharacters(in: .whitespacesAndNewlines),
              !normalized.isEmpty
        else {
            return nil
        }
        return truncate(normalized, maximumLength: maximumLength)
    }

    private static func truncate(_ value: String, maximumLength: Int) -> String {
        guard value.count > maximumLength else {
            return value
        }
        return String(value.prefix(maximumLength)) + "…"
    }
}
