using System.Text.RegularExpressions;
using System.Text;

namespace Ansight.Network;

/// <summary>
/// Applies mandatory and app-configured privacy controls to captured network metadata.
/// </summary>
public static class NetworkRequestSanitizer
{
    internal const string RedactedValue = "<redacted>";
    private const int MaximumHeaderCount = 128;
    private const int MaximumHeaderValueLength = 4096;
    private const int MaximumErrorMessageLength = 4096;
    private const int MaximumUrlLength = 16_384;

    private static readonly HashSet<string> sensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
        "Set-Cookie",
        "X-Api-Key",
        "X-Auth-Token"
    };

    private static readonly HashSet<string> sensitiveQueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
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
        "token"
    };
    private static readonly HashSet<string> azureSasFingerprintNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "se", "sig", "skoid", "sp", "sr", "srt", "ss", "sv"
    };
    private static readonly HashSet<string> azureSasQueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "epk", "erk", "rscc", "rscd", "rsce", "rscl", "rsct", "saoid", "scid", "se",
        "sig", "si", "sip", "ske", "skoid", "sks", "skt", "sktid", "skv", "snapshot",
        "sp", "spk", "spr", "sr", "srk", "srt", "ss", "st", "suoid", "tn", "versionid", "sv"
    };
    private static readonly UTF8Encoding strictUtf8Encoding = new(false, true);
    private static readonly Regex sensitiveAssignmentPattern = new(
        @"(?<name>access_token|api_key|apikey|auth|authorization|code|key|password|passwd|secret|signature|token)(?<separator>[""']?\s*[:=]\s*[""']?)(?<value>[^&\s,;}""']+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex absoluteUrlPattern = new(
        @"https?://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Sanitizes a request before capture. A <see langword="null"/> result means the app suppressed the request.
    /// </summary>
    public static NetworkRequestRecord? Sanitize(
        NetworkRequestRecord request,
        NetworkRequestSanitizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var normalized = NormalizeCore(request, options, preserveCapturedBodies: false);
            if (options?.UrlSanitizer is not null)
            {
                normalized = NormalizeCore(
                    normalized with { Url = options.UrlSanitizer(normalized.Url) },
                    options,
                    preserveCapturedBodies: false);
            }

            if (options?.RequestSanitizer is null)
            {
                return normalized;
            }

            var transformed = options.RequestSanitizer(normalized);
            return transformed is null
                ? null
                : NormalizeCore(transformed, options, preserveCapturedBodies: false);
        }
        catch
        {
            // App sanitizers must never affect the HTTP request. Fail closed by omitting capture.
            return null;
        }
    }

    internal static NetworkRequestRecord SanitizeForTransport(NetworkRequestRecord request)
        => NormalizeCore(request, null, preserveCapturedBodies: true);

    private static NetworkRequestRecord NormalizeCore(
        NetworkRequestRecord request,
        NetworkRequestSanitizationOptions? options,
        bool preserveCapturedBodies)
    {
        var maximumBodyBytes = preserveCapturedBodies ? int.MaxValue : MaximumBodyBytes(options);
        var startedAtUtc = request.StartedAtUtc.ToUniversalTime();
        var completedAtUtc = request.CompletedAtUtc.ToUniversalTime();
        if (completedAtUtc < startedAtUtc)
        {
            completedAtUtc = startedAtUtc;
        }

        return new NetworkRequestRecord
        {
            Id = NormalizeRequired(request.Id, Guid.CreateVersion7().ToString("N"), 128),
            Source = NormalizeRequired(request.Source, "unknown", 128),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationMilliseconds = double.IsFinite(request.DurationMilliseconds)
                ? Math.Max(0, request.DurationMilliseconds)
                : Math.Max(0, (completedAtUtc - startedAtUtc).TotalMilliseconds),
            Method = NormalizeRequired(request.Method, "GET", 32).ToUpperInvariant(),
            Url = SanitizeUrl(request.Url, options),
            Protocol = NormalizeOptional(request.Protocol, 64),
            RequestHeaders = options?.IncludeRequestHeaders == false
                ? Array.Empty<NetworkHeader>()
                : SanitizeHeaders(request.RequestHeaders, options),
            RequestBodySizeBytes = options?.IncludeBodySizes == false
                ? null
                : NormalizeSize(request.RequestBodySizeBytes),
            RequestBody = preserveCapturedBodies || options?.CaptureRequestBody != false
                ? SanitizeBody(request.RequestBody, maximumBodyBytes, options, preserveCapturedBodies)
                : null,
            StatusCode = request.StatusCode is >= 100 and <= 999 ? request.StatusCode : null,
            ReasonPhrase = NormalizeOptional(request.ReasonPhrase, 512),
            ResponseHeaders = options?.IncludeResponseHeaders == false
                ? Array.Empty<NetworkHeader>()
                : SanitizeHeaders(request.ResponseHeaders, options),
            ResponseBodySizeBytes = options?.IncludeBodySizes == false
                ? null
                : NormalizeSize(request.ResponseBodySizeBytes),
            ResponseBody = preserveCapturedBodies || options?.CaptureResponseBody != false
                ? SanitizeBody(request.ResponseBody, maximumBodyBytes, options, preserveCapturedBodies)
                : null,
            ErrorType = NormalizeOptional(request.ErrorType, 512),
            ErrorMessage = SanitizeErrorMessage(request.ErrorMessage, options)
        };
    }

    internal static int MaximumBodyBytes(NetworkRequestSanitizationOptions? options)
        => Math.Max(0, options?.MaximumBodyBytes ?? 64 * 1024);

    internal static NetworkBody? CreateBody(
        ReadOnlySpan<byte> bytes,
        long? totalBytes,
        string? contentType,
        bool binary,
        NetworkRequestSanitizationOptions options)
    {
        var maximumBytes = MaximumBodyBytes(options);
        if (maximumBytes <= 0 || binary && !options.CaptureBinaryBodies)
        {
            return null;
        }

        var capturedLength = Math.Min(bytes.Length, maximumBytes);
        var captured = bytes[..capturedLength].ToArray();
        if (!binary)
        {
            captured = EnsureCompleteUtf8(captured);
        }

        var normalizedTotalBytes = NormalizeSize(totalBytes);

        return new NetworkBody
        {
            ContentType = NormalizeOptional(contentType, 512),
            Encoding = binary ? NetworkBody.Base64Encoding : NetworkBody.Utf8Encoding,
            Data = binary ? Convert.ToBase64String(captured) : Encoding.UTF8.GetString(captured),
            CapturedBytes = captured.Length,
            TotalBytes = normalizedTotalBytes,
            Truncated = bytes.Length > captured.Length
                        || normalizedTotalBytes is not null && normalizedTotalBytes.Value > captured.Length
        };
    }

    internal static bool IsTextContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
               || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
               || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("application/graphql", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
               || mediaType.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
    }

    private static NetworkBody? SanitizeBody(
        NetworkBody? body,
        int maximumBytes,
        NetworkRequestSanitizationOptions? options,
        bool preserveCapturedBodies)
    {
        if (body is null || maximumBytes <= 0)
        {
            return null;
        }

        byte[] decoded;
        var encoding = body.Encoding.Trim().ToLowerInvariant();
        try
        {
            decoded = encoding switch
            {
                NetworkBody.Utf8Encoding => Encoding.UTF8.GetBytes(SanitizeSensitiveText(body.Data, options)),
                NetworkBody.Base64Encoding => Convert.FromBase64String(body.Data),
                _ => Array.Empty<byte>()
            };
        }
        catch
        {
            return null;
        }

        if (encoding is not (NetworkBody.Utf8Encoding or NetworkBody.Base64Encoding))
        {
            return null;
        }
        if (encoding == NetworkBody.Base64Encoding
            && !preserveCapturedBodies
            && options?.CaptureBinaryBodies != true)
        {
            return null;
        }

        var originalLength = decoded.Length;
        if (decoded.Length > maximumBytes)
        {
            decoded = decoded[..maximumBytes];
        }
        if (encoding == NetworkBody.Utf8Encoding)
        {
            decoded = EnsureCompleteUtf8(decoded);
        }

        var totalBytes = NormalizeSize(body.TotalBytes);
        return new NetworkBody
        {
            ContentType = NormalizeOptional(body.ContentType, 512),
            Encoding = encoding,
            Data = encoding == NetworkBody.Base64Encoding
                ? Convert.ToBase64String(decoded)
                : Encoding.UTF8.GetString(decoded),
            CapturedBytes = decoded.Length,
            TotalBytes = totalBytes,
            Truncated = body.Truncated
                        || originalLength > decoded.Length
                        || totalBytes is not null && totalBytes.Value > decoded.Length
        };
    }

    private static string SanitizeSensitiveText(
        string value,
        NetworkRequestSanitizationOptions? options)
    {
        var assignmentsRedacted = sensitiveAssignmentPattern.Replace(
            value,
            match => $"{match.Groups["name"].Value}{match.Groups["separator"].Value}{RedactedValue}");
        return absoluteUrlPattern.Replace(assignmentsRedacted, match => SanitizeUrl(match.Value, options));
    }

    private static byte[] EnsureCompleteUtf8(byte[] bytes)
    {
        var length = bytes.Length;
        while (length > 0)
        {
            try
            {
                _ = strictUtf8Encoding.GetString(bytes, 0, length);
                return length == bytes.Length ? bytes : bytes[..length];
            }
            catch (DecoderFallbackException)
            {
                length--;
            }
        }

        return Array.Empty<byte>();
    }

    internal static IReadOnlyList<NetworkHeader> SanitizeHeaders(IEnumerable<NetworkHeader>? headers)
        => SanitizeHeaders(headers, null);

    private static IReadOnlyList<NetworkHeader> SanitizeHeaders(
        IEnumerable<NetworkHeader>? headers,
        NetworkRequestSanitizationOptions? options)
    {
        if (headers is null)
        {
            return Array.Empty<NetworkHeader>();
        }

        return headers
            .Where(header => header is not null && !string.IsNullOrWhiteSpace(header.Name))
            .Take(MaximumHeaderCount)
            .Select(header =>
            {
                var name = NormalizeRequired(header.Name, "Header", 256);
                return new NetworkHeader
                {
                    Name = name,
                    Value = IsSensitiveHeader(name, options)
                        ? RedactedValue
                        : NormalizeRequired(header.Value, string.Empty, MaximumHeaderValueLength)
                };
            })
            .ToArray();
    }

    private static string SanitizeUrl(
        string? value,
        NetworkRequestSanitizationOptions? options)
    {
        var normalized = NormalizeRequired(value, "<unknown>", MaximumUrlLength);
        if (!Uri.TryCreate(normalized, UriKind.RelativeOrAbsolute, out var uri)
            || !uri.IsAbsoluteUri)
        {
            return SanitizeRelativeUrl(normalized, options);
        }

        try
        {
            var builder = new UriBuilder(uri)
            {
                UserName = string.IsNullOrEmpty(uri.UserInfo) ? string.Empty : RedactedValue,
                Password = string.Empty,
                Query = options?.IncludeQueryString == false
                    ? string.Empty
                    : SanitizeQuery(uri.Query, options)
            };
            return Truncate(builder.Uri.AbsoluteUri, MaximumUrlLength);
        }
        catch
        {
            return Truncate(normalized, MaximumUrlLength);
        }
    }

    private static string SanitizeRelativeUrl(
        string value,
        NetworkRequestSanitizationOptions? options)
    {
        var queryIndex = value.IndexOf('?');
        if (queryIndex < 0)
        {
            return Truncate(value, MaximumUrlLength);
        }

        var fragmentIndex = value.IndexOf('#', queryIndex);
        if (options?.IncludeQueryString == false)
        {
            return Truncate(
                fragmentIndex < 0 ? value[..queryIndex] : value[..queryIndex] + value[fragmentIndex..],
                MaximumUrlLength);
        }

        var query = fragmentIndex < 0
            ? value[(queryIndex + 1)..]
            : value[(queryIndex + 1)..fragmentIndex];
        var fragment = fragmentIndex < 0 ? string.Empty : value[fragmentIndex..];
        return Truncate($"{value[..queryIndex]}?{SanitizeQuery(query, options)}{fragment}", MaximumUrlLength);
    }

    private static string SanitizeQuery(
        string query,
        NetworkRequestSanitizationOptions? options)
    {
        var normalized = query.TrimStart('?');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var pairs = normalized.Split('&');
        var decodedNames = pairs
            .Select(GetDecodedQueryName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasAzureSas = decodedNames.Contains("sig")
                          && decodedNames.Overlaps(azureSasFingerprintNames.Where(name => name != "sig"));
        var hasAwsSignature = decodedNames.Contains("x-amz-signature");
        var hasGoogleSignature = decodedNames.Contains("x-goog-signature");
        var hasCloudFrontSignature = decodedNames.Contains("signature")
                                     && (decodedNames.Contains("key-pair-id")
                                         || decodedNames.Contains("policy")
                                         || decodedNames.Contains("expires"));
        var hasLegacyGoogleSignature = decodedNames.Contains("signature")
                                       && decodedNames.Contains("googleaccessid");
        var hasAlibabaSignature = (decodedNames.Contains("signature")
                                   && decodedNames.Contains("ossaccesskeyid"))
                                  || decodedNames.Contains("x-oss-signature");

        return string.Join("&", pairs.Select(pair =>
        {
            var equalsIndex = pair.IndexOf('=');
            var encodedName = equalsIndex < 0 ? pair : pair[..equalsIndex];
            var decodedName = GetDecodedQueryName(pair);
            var providerSensitive = hasAzureSas && azureSasQueryNames.Contains(decodedName)
                                    || hasAwsSignature && decodedName.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase)
                                    || hasGoogleSignature && decodedName.StartsWith("x-goog-", StringComparison.OrdinalIgnoreCase)
                                    || hasCloudFrontSignature && decodedName is "signature" or "key-pair-id" or "policy" or "expires" or "hash-algorithm"
                                    || hasLegacyGoogleSignature && decodedName is "signature" or "googleaccessid" or "expires"
                                    || hasAlibabaSignature && (decodedName.StartsWith("x-oss-", StringComparison.OrdinalIgnoreCase)
                                                               || decodedName is "signature" or "ossaccesskeyid" or "security-token");
            if (!providerSensitive
                && !sensitiveQueryNames.Contains(decodedName)
                && options?.AdditionalSensitiveQueryParameterNames.Contains(
                    decodedName,
                    StringComparer.OrdinalIgnoreCase) != true)
            {
                return pair;
            }

            return $"{encodedName}={Uri.EscapeDataString(RedactedValue)}";
        }));
    }

    private static string GetDecodedQueryName(string pair)
    {
        var equalsIndex = pair.IndexOf('=');
        var encodedName = equalsIndex < 0 ? pair : pair[..equalsIndex];
        try
        {
            return Uri.UnescapeDataString(encodedName.Replace("+", " "));
        }
        catch
        {
            return encodedName;
        }
    }

    private static bool IsSensitiveHeader(
        string name,
        NetworkRequestSanitizationOptions? options)
    {
        if (sensitiveHeaderNames.Contains(name)
            || options?.AdditionalSensitiveHeaderNames.Contains(
                name,
                StringComparer.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        var normalized = name.Replace("-", string.Empty, StringComparison.Ordinal);
        return normalized.Contains("token", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("secret", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("apikey", StringComparison.OrdinalIgnoreCase);
    }

    private static long? NormalizeSize(long? value) => value is >= 0 ? value : null;

    private static string? SanitizeErrorMessage(
        string? value,
        NetworkRequestSanitizationOptions? options)
    {
        var normalized = NormalizeOptional(value, MaximumErrorMessageLength);
        if (normalized is null)
        {
            return null;
        }

        var assignmentsRedacted = sensitiveAssignmentPattern.Replace(
            normalized,
            match => $"{match.Groups["name"].Value}{match.Groups["separator"].Value}{RedactedValue}");
        var urlsRedacted = absoluteUrlPattern.Replace(
            assignmentsRedacted,
            match => SanitizeUrl(match.Value, options));
        return Truncate(urlsRedacted, MaximumErrorMessageLength);
    }

    private static string NormalizeRequired(string? value, string fallback, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return Truncate(normalized, maximumLength);
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Truncate(value.Trim(), maximumLength);
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength
            ? value
            : value[..maximumLength] + "…";
    }
}
