"use strict";

const REDACTED_VALUE = "<redacted>";
const SCHEMA = "ansight.network-request.v1";
const MAXIMUM_HEADER_COUNT = 128;
const MAXIMUM_HEADER_VALUE_LENGTH = 4096;
const MAXIMUM_ERROR_MESSAGE_LENGTH = 4096;
const MAXIMUM_URL_LENGTH = 16384;
const DEFAULT_MAXIMUM_BODY_BYTES = 64 * 1024;
const SENSITIVE_HEADER_NAMES = new Set([
  "authorization",
  "cookie",
  "proxy-authorization",
  "set-cookie",
  "x-api-key",
  "x-auth-token",
]);
const SENSITIVE_QUERY_NAMES = new Set([
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
]);
const AZURE_SAS_FINGERPRINT_NAMES = new Set(["se", "skoid", "sp", "sr", "srt", "ss", "sv"]);
const AZURE_SAS_QUERY_NAMES = new Set([
  "epk", "erk", "rscc", "rscd", "rsce", "rscl", "rsct", "saoid", "scid", "se",
  "sig", "si", "sip", "ske", "skoid", "sks", "skt", "sktid", "skv", "snapshot",
  "sp", "spk", "spr", "sr", "srk", "srt", "ss", "st", "suoid", "tn", "versionid", "sv",
]);

function truncate(value, maximumLength) {
  const text = String(value);
  return text.length <= maximumLength
    ? text
    : `${text.slice(0, maximumLength)}…`;
}

function normalizeRequired(value, fallback, maximumLength) {
  const normalized = value == null ? "" : String(value).trim();
  return truncate(normalized || fallback, maximumLength);
}

function normalizeOptional(value, maximumLength) {
  if (value == null) return undefined;
  const normalized = String(value).trim();
  return normalized ? truncate(normalized, maximumLength) : undefined;
}

function uniqueLowercase(values) {
  return new Set(
    Array.from(values || [], (value) => String(value).toLowerCase()),
  );
}

function isSensitiveHeader(name, options) {
  const lowered = name.toLowerCase();
  if (
    SENSITIVE_HEADER_NAMES.has(lowered) ||
    uniqueLowercase(options.additionalSensitiveHeaderNames).has(lowered)
  ) {
    return true;
  }
  const compact = lowered.replace(/-/g, "");
  return (
    compact.includes("token") ||
    compact.includes("secret") ||
    compact.includes("apikey")
  );
}

function headerEntries(headers) {
  if (!headers) return [];
  if (Array.isArray(headers)) {
    return headers.flatMap((header) => {
      if (Array.isArray(header)) return [[header[0], header[1]]];
      if (header && typeof header === "object") {
        return [[header.name, header.value]];
      }
      return [];
    });
  }
  if (typeof headers.forEach === "function") {
    const entries = [];
    headers.forEach((value, name) => entries.push([name, value]));
    return entries;
  }
  if (typeof headers === "object") return Object.entries(headers);
  return [];
}

function sanitizeHeaders(headers, options) {
  return headerEntries(headers)
    .filter(([name]) => name != null && String(name).trim())
    .slice(0, MAXIMUM_HEADER_COUNT)
    .map(([rawName, rawValue]) => {
      const name = normalizeRequired(rawName, "Header", 256);
      return {
        name,
        value: isSensitiveHeader(name, options)
          ? REDACTED_VALUE
          : normalizeRequired(rawValue, "", MAXIMUM_HEADER_VALUE_LENGTH),
      };
    });
}

function isSensitiveQueryName(name, options, schemes = {}) {
  const lowered = String(name).toLowerCase();
  return (
    SENSITIVE_QUERY_NAMES.has(lowered) ||
    uniqueLowercase(options.additionalSensitiveQueryParameterNames).has(lowered) ||
    (schemes.azure && AZURE_SAS_QUERY_NAMES.has(lowered)) ||
    (schemes.aws && lowered.startsWith("x-amz-")) ||
    (schemes.google && lowered.startsWith("x-goog-")) ||
    (schemes.cloudFront && ["signature", "key-pair-id", "policy", "expires", "hash-algorithm"].includes(lowered)) ||
    (schemes.legacyGoogle && ["signature", "googleaccessid", "expires"].includes(lowered)) ||
    (schemes.alibaba && (lowered.startsWith("x-oss-") || ["signature", "ossaccesskeyid", "security-token"].includes(lowered)))
  );
}

function sanitizeQuery(query, options) {
  if (!query) return "";
  const pairs = query.split("&");
  const decodedNames = new Set(pairs.map((pair) => decodeQueryName(pair).toLowerCase()));
  const schemes = {
    azure: decodedNames.has("sig") && Array.from(AZURE_SAS_FINGERPRINT_NAMES).some((name) => decodedNames.has(name)),
    aws: decodedNames.has("x-amz-signature"),
    google: decodedNames.has("x-goog-signature"),
    cloudFront: decodedNames.has("signature") && ["key-pair-id", "policy", "expires"].some((name) => decodedNames.has(name)),
    legacyGoogle: decodedNames.has("signature") && decodedNames.has("googleaccessid"),
    alibaba: (decodedNames.has("signature") && decodedNames.has("ossaccesskeyid")) || decodedNames.has("x-oss-signature"),
  };
  return pairs
    .map((pair) => {
      const equalsIndex = pair.indexOf("=");
      const encodedName = equalsIndex < 0 ? pair : pair.slice(0, equalsIndex);
      return isSensitiveQueryName(decodeQueryName(pair), options, schemes)
        ? `${encodedName}=${encodeURIComponent(REDACTED_VALUE)}`
        : pair;
    })
    .join("&");
}

function decodeQueryName(pair) {
  const equalsIndex = pair.indexOf("=");
  const encodedName = equalsIndex < 0 ? pair : pair.slice(0, equalsIndex);
  try {
    return decodeURIComponent(encodedName.replace(/\+/g, " "));
  } catch (_) {
    return encodedName;
  }
}

function sanitizeUrl(value, options = {}) {
  const normalized = normalizeRequired(value, "<unknown>", MAXIMUM_URL_LENGTH);
  let withoutUserInfo = normalized.replace(
    /^(https?:\/\/)[^/@]+@/i,
    `$1${REDACTED_VALUE}@`,
  );
  const queryIndex = withoutUserInfo.indexOf("?");
  if (queryIndex < 0) return truncate(withoutUserInfo, MAXIMUM_URL_LENGTH);
  const fragmentIndex = withoutUserInfo.indexOf("#", queryIndex);
  if (options.includeQueryString === false) {
    withoutUserInfo =
      withoutUserInfo.slice(0, queryIndex) +
      (fragmentIndex < 0 ? "" : withoutUserInfo.slice(fragmentIndex));
    return truncate(withoutUserInfo, MAXIMUM_URL_LENGTH);
  }
  const queryEnd = fragmentIndex < 0 ? withoutUserInfo.length : fragmentIndex;
  const sanitized =
    withoutUserInfo.slice(0, queryIndex + 1) +
    sanitizeQuery(withoutUserInfo.slice(queryIndex + 1, queryEnd), options) +
    (fragmentIndex < 0 ? "" : withoutUserInfo.slice(fragmentIndex));
  return truncate(sanitized, MAXIMUM_URL_LENGTH);
}

function sanitizeErrorMessage(value, options) {
  const normalized = normalizeOptional(value, MAXIMUM_ERROR_MESSAGE_LENGTH);
  if (!normalized) return undefined;
  return truncate(
    normalized
      .replace(
        /(access_token|api_key|apikey|auth|authorization|code|key|password|passwd|secret|signature|token)(\s*=\s*)([^&\s,;]+)/gi,
        `$1$2${REDACTED_VALUE}`,
      )
      .replace(/https?:\/\/[^\s"'<>]+/gi, (url) =>
        sanitizeUrl(url, options),
      ),
    MAXIMUM_ERROR_MESSAGE_LENGTH,
  );
}

function normalizeTimestamp(value, fallback) {
  const date = new Date(value || fallback);
  return Number.isFinite(date.valueOf()) ? date.toISOString() : fallback;
}

function generateId(globalObject) {
  if (globalObject.crypto && typeof globalObject.crypto.randomUUID === "function") {
    return globalObject.crypto.randomUUID().replace(/-/g, "");
  }
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
}

function normalizeSize(value) {
  const number = Number(value);
  return Number.isFinite(number) && number >= 0 ? Math.round(number) : undefined;
}

function maximumBodyBytes(options) {
  const configured = Number(options.maximumBodyBytes);
  const value = Number.isFinite(configured) ? Math.round(configured) : DEFAULT_MAXIMUM_BODY_BYTES;
  return Math.max(0, value);
}

function encodeUtf8(value, globalObject) {
  if (typeof globalObject.TextEncoder === "function") return new globalObject.TextEncoder().encode(value);
  const encoded = unescape(encodeURIComponent(value));
  return Uint8Array.from(encoded, (character) => character.charCodeAt(0));
}

function decodeUtf8(bytes, globalObject) {
  if (typeof globalObject.TextDecoder === "function") {
    return new globalObject.TextDecoder("utf-8", { fatal: false }).decode(bytes);
  }
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  try { return decodeURIComponent(escape(binary)); } catch (_) { return ""; }
}

function bytesToBase64(bytes, globalObject) {
  if (typeof globalObject.btoa !== "function") return undefined;
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return globalObject.btoa(binary);
}

function base64ToBytes(value, globalObject) {
  if (typeof globalObject.atob !== "function") return undefined;
  const binary = globalObject.atob(value);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

function sanitizeSensitiveText(value, options) {
  return String(value)
    .replace(
      /(access_token|accesskey|access_key|api_key|apikey|auth|authorization|client_secret|code|credential|credentials|id_token|jwt|key|password|passwd|refresh_token|sas|sastoken|secret|secret_key|security_token|session_token|sig|signature|token)(["']?\s*[:=]\s*["']?)([^&\s,;}"']+)/gi,
      `$1$2${REDACTED_VALUE}`,
    )
    .replace(/https?:\/\/[^\s"'<>]+/gi, (url) => sanitizeUrl(url, options));
}

function normalizeBody(body, options, globalObject) {
  if (!body || maximumBodyBytes(options) <= 0) return undefined;
  const maximum = maximumBodyBytes(options);
  const encoding = String(body.encoding || "").toLowerCase();
  let bytes;
  if (encoding === "utf8") {
    bytes = encodeUtf8(sanitizeSensitiveText(body.data || "", options), globalObject);
  } else if (encoding === "base64" && options.captureBinaryBodies === true) {
    try { bytes = base64ToBytes(String(body.data || ""), globalObject); } catch (_) { return undefined; }
  } else {
    return undefined;
  }
  if (!bytes) return undefined;
  const originalLength = bytes.length;
  const captured = bytes.slice(0, maximum);
  const data = encoding === "base64"
    ? bytesToBase64(captured, globalObject)
    : decodeUtf8(captured, globalObject);
  if (data == null) return undefined;
  const totalBytes = normalizeSize(body.totalBytes);
  return {
    contentType: normalizeOptional(body.contentType, 512),
    encoding,
    data,
    capturedBytes: encoding === "utf8"
      ? encodeUtf8(data, globalObject).length
      : captured.length,
    totalBytes,
    truncated: body.truncated === true || originalLength > captured.length || (totalBytes != null && totalBytes > captured.length),
  };
}

function normalizeRecord(input, options, globalObject) {
  const now = new Date().toISOString();
  const startedAtUtc = normalizeTimestamp(input.startedAtUtc, now);
  const completedAtUtc = normalizeTimestamp(input.completedAtUtc, startedAtUtc);
  const duration = Number(input.durationMilliseconds);
  return {
    schema: SCHEMA,
    id: normalizeRequired(input.id, generateId(globalObject), 128),
    source: normalizeRequired(input.source, "unknown", 128),
    startedAtUtc,
    completedAtUtc:
      completedAtUtc < startedAtUtc ? startedAtUtc : completedAtUtc,
    durationMilliseconds:
      Number.isFinite(duration) && duration >= 0 ? duration : 0,
    method: normalizeRequired(input.method, "GET", 32).toUpperCase(),
    url: sanitizeUrl(input.url, options),
    protocol: normalizeOptional(input.protocol, 64),
    requestHeaders:
      options.includeRequestHeaders === false
        ? []
        : sanitizeHeaders(input.requestHeaders, options),
    requestBodySizeBytes:
      options.includeBodySizes === false
        ? undefined
        : normalizeSize(input.requestBodySizeBytes),
    requestBody: options.captureRequestBody !== false
      ? normalizeBody(input.requestBody, options, globalObject)
      : undefined,
    statusCode:
      Number.isInteger(Number(input.statusCode)) &&
      Number(input.statusCode) >= 100 &&
      Number(input.statusCode) <= 999
        ? Number(input.statusCode)
        : undefined,
    reasonPhrase: normalizeOptional(input.reasonPhrase, 512),
    responseHeaders:
      options.includeResponseHeaders === false
        ? []
        : sanitizeHeaders(input.responseHeaders, options),
    responseBodySizeBytes:
      options.includeBodySizes === false
        ? undefined
        : normalizeSize(input.responseBodySizeBytes),
    responseBody: options.captureResponseBody !== false
      ? normalizeBody(input.responseBody, options, globalObject)
      : undefined,
    errorType: normalizeOptional(input.errorType, 512),
    errorMessage: sanitizeErrorMessage(input.errorMessage, options),
  };
}

function sanitizeNetworkRequest(input, options = {}, globalObject = global) {
  try {
    let normalized = normalizeRecord(input || {}, options, globalObject);
    if (typeof options.urlSanitizer === "function") {
      normalized = normalizeRecord(
        { ...normalized, url: options.urlSanitizer(normalized.url) },
        options,
        globalObject,
      );
    }
    if (typeof options.requestSanitizer === "function") {
      const transformed = options.requestSanitizer(normalized);
      if (transformed == null) return null;
      normalized = normalizeRecord(transformed, options, globalObject);
    }
    return normalized;
  } catch (_) {
    // App sanitizers must never affect the HTTP request. Fail closed.
    return null;
  }
}

function parseContentLength(headers) {
  const entry = headerEntries(headers).find(
    ([name]) => String(name).toLowerCase() === "content-length",
  );
  return entry ? normalizeSize(entry[1]) : undefined;
}

function headerValue(headers, wantedName) {
  const entry = headerEntries(headers).find(
    ([name]) => String(name).toLowerCase() === wantedName,
  );
  return entry == null ? undefined : String(entry[1]);
}

function isTextContentType(contentType) {
  if (!contentType) return true;
  const mediaType = contentType.split(";", 1)[0].trim().toLowerCase();
  return mediaType.startsWith("text/") || mediaType.endsWith("+json") || mediaType.endsWith("+xml") ||
    ["application/json", "application/xml", "application/graphql", "application/javascript", "application/x-www-form-urlencoded"].includes(mediaType);
}

function bodyFromBytes(bytes, totalBytes, contentType, options, globalObject) {
  const binary = !isTextContentType(contentType);
  if (binary && options.captureBinaryBodies !== true) return undefined;
  const maximum = maximumBodyBytes(options);
  if (maximum <= 0) return undefined;
  const captured = bytes.slice(0, maximum);
  const data = binary ? bytesToBase64(captured, globalObject) : decodeUtf8(captured, globalObject);
  if (data == null) return undefined;
  return {
    contentType,
    encoding: binary ? "base64" : "utf8",
    data,
    capturedBytes: binary ? captured.length : encodeUtf8(data, globalObject).length,
    totalBytes,
    truncated: bytes.length > captured.length || (totalBytes != null && totalBytes > captured.length),
  };
}

function bodyFromValue(value, headers, options, globalObject) {
  if (value == null || options.captureRequestBody === false) return undefined;
  const contentType = headerValue(headers, "content-type");
  if (typeof value === "string") {
    const bytes = encodeUtf8(value, globalObject);
    return bodyFromBytes(bytes, bytes.length, contentType, options, globalObject);
  }
  if (typeof globalObject.URLSearchParams === "function" && value instanceof globalObject.URLSearchParams) {
    const bytes = encodeUtf8(value.toString(), globalObject);
    return bodyFromBytes(bytes, bytes.length, contentType || "application/x-www-form-urlencoded", options, globalObject);
  }
  if (typeof globalObject.ArrayBuffer === "function") {
    if (value instanceof globalObject.ArrayBuffer) {
      const bytes = new Uint8Array(value);
      return bodyFromBytes(bytes, bytes.length, contentType, options, globalObject);
    }
    if (globalObject.ArrayBuffer.isView && globalObject.ArrayBuffer.isView(value)) {
      const bytes = new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
      return bodyFromBytes(bytes, bytes.length, contentType, options, globalObject);
    }
  }
  return undefined;
}

async function bodyFromFetchResponse(
  response,
  headers,
  options,
  globalObject,
  shouldContinue = () => true,
) {
  if (!shouldContinue() || options.captureResponseBody === false || !response || typeof response.clone !== "function") return undefined;
  const contentType = headerValue(headers, "content-type");
  if (!isTextContentType(contentType) && options.captureBinaryBodies !== true) return undefined;
  const totalBytes = parseContentLength(headers);
  const maximum = maximumBodyBytes(options);
  if (maximum <= 0) return undefined;
  const clone = response.clone();
  if (clone.body && typeof clone.body.getReader === "function") {
    const reader = clone.body.getReader();
    const chunks = [];
    let capturedLength = 0;
    let observedLength = 0;
    try {
      while (capturedLength <= maximum) {
        if (!shouldContinue()) {
          await reader.cancel().catch(() => undefined);
          return undefined;
        }
        const result = await reader.read();
        if (!shouldContinue()) {
          await reader.cancel().catch(() => undefined);
          return undefined;
        }
        if (result.done) break;
        const chunk = result.value instanceof Uint8Array ? result.value : new Uint8Array(result.value);
        observedLength += chunk.length;
        const remaining = maximum - capturedLength;
        if (remaining > 0) {
          const kept = chunk.slice(0, remaining);
          chunks.push(kept);
          capturedLength += kept.length;
        }
        if (observedLength > maximum) {
          await reader.cancel().catch(() => undefined);
          break;
        }
      }
    } finally {
      try { reader.releaseLock(); } catch (_) { /* no-op */ }
    }
    const joined = new Uint8Array(capturedLength);
    let offset = 0;
    for (const chunk of chunks) { joined.set(chunk, offset); offset += chunk.length; }
    return bodyFromBytes(joined, totalBytes == null ? observedLength : totalBytes, contentType, options, globalObject);
  }
  if (totalBytes == null || totalBytes > maximum || typeof clone.arrayBuffer !== "function") return undefined;
  const bytes = new Uint8Array(await clone.arrayBuffer());
  if (!shouldContinue()) return undefined;
  return bodyFromBytes(bytes, totalBytes, contentType, options, globalObject);
}

function parseXhrResponseHeaders(value) {
  if (!value) return [];
  return String(value)
    .trim()
    .split(/[\r\n]+/)
    .flatMap((line) => {
      const separator = line.indexOf(":");
      return separator < 0
        ? []
        : [{ name: line.slice(0, separator), value: line.slice(separator + 1) }];
    });
}

function monotonicNow(globalObject) {
  return globalObject.performance && typeof globalObject.performance.now === "function"
    ? globalObject.performance.now()
    : Date.now();
}

function emitRecord(record, options, globalObject, capture) {
  const sanitized = sanitizeNetworkRequest(record, options, globalObject);
  if (!sanitized) return;
  try {
    const result = capture(sanitized);
    if (result && typeof result.catch === "function") result.catch(() => undefined);
  } catch (_) {
    // Observability must not affect application networking.
  }
}

function installNetworkCapture({
  globalObject = global,
  options = {},
  sourcePrefix = "react-native",
  capture,
}) {
  if (typeof capture !== "function") {
    throw new TypeError("installNetworkCapture requires a capture callback.");
  }

  const cleanups = [];
  let active = true;
  let fetchInvocationDepth = 0;

  if (options.captureFetch !== false && typeof globalObject.fetch === "function") {
    const originalFetch = globalObject.fetch;
    const wrappedFetch = function ansightFetch(input, init) {
      const startedAtUtc = new Date().toISOString();
      const started = monotonicNow(globalObject);
      const inputHeaders = input && typeof input === "object" ? input.headers : undefined;
      const requestHeaders = headerEntries(inputHeaders).concat(headerEntries(init && init.headers));
      const requestBody = bodyFromValue(init && init.body, requestHeaders, options, globalObject);
      const method = (init && init.method) || (input && input.method) || "GET";
      const url = typeof input === "string" ? input : input && input.url ? input.url : String(input);
      let promise;
      fetchInvocationDepth += 1;
      try {
        promise = originalFetch.apply(this, arguments);
      } catch (error) {
        fetchInvocationDepth -= 1;
        if (active) emitRecord(
          {
            source: `${sourcePrefix}.fetch`,
            startedAtUtc,
            completedAtUtc: new Date().toISOString(),
            durationMilliseconds: monotonicNow(globalObject) - started,
            method,
            url,
            requestHeaders,
            requestBodySizeBytes: parseContentLength(requestHeaders) ?? (requestBody && requestBody.totalBytes),
            requestBody,
            errorType: error && error.name,
            errorMessage: error && error.message ? error.message : String(error),
          },
          options,
          globalObject,
          capture,
        );
        throw error;
      }
      fetchInvocationDepth -= 1;
      return Promise.resolve(promise).then(
        (response) => {
          if (!active) return response;
          const responseHeaders = headerEntries(response && response.headers);
          const responseRecord = {
              source: `${sourcePrefix}.fetch`,
              startedAtUtc,
              completedAtUtc: new Date().toISOString(),
              durationMilliseconds: monotonicNow(globalObject) - started,
              method,
              url: (response && response.url) || url,
              requestHeaders,
              requestBodySizeBytes: parseContentLength(requestHeaders) ?? (requestBody && requestBody.totalBytes),
              requestBody,
              statusCode: response && response.status,
              reasonPhrase: response && response.statusText,
              responseHeaders,
              responseBodySizeBytes: parseContentLength(responseHeaders),
            };
          return bodyFromFetchResponse(
            response,
            responseHeaders,
            options,
            globalObject,
            () => active,
          ).then(
            (responseBody) => {
              if (active) emitRecord(
                { ...responseRecord, responseBody, responseBodySizeBytes: responseRecord.responseBodySizeBytes ?? (responseBody && responseBody.totalBytes) },
                options,
                globalObject,
                capture,
              );
              return response;
            },
            () => {
              if (active) emitRecord(responseRecord, options, globalObject, capture);
              return response;
            },
          );
        },
        (error) => {
          if (active) emitRecord(
            {
              source: `${sourcePrefix}.fetch`,
              startedAtUtc,
              completedAtUtc: new Date().toISOString(),
              durationMilliseconds: monotonicNow(globalObject) - started,
              method,
              url,
              requestHeaders,
              requestBodySizeBytes: parseContentLength(requestHeaders) ?? (requestBody && requestBody.totalBytes),
              requestBody,
              errorType: error && error.name,
              errorMessage: error && error.message ? error.message : String(error),
            },
            options,
            globalObject,
            capture,
          );
          throw error;
        },
      );
    };
    globalObject.fetch = wrappedFetch;
    cleanups.push(() => {
      if (globalObject.fetch === wrappedFetch) globalObject.fetch = originalFetch;
    });
  }

  const Xhr = globalObject.XMLHttpRequest;
  if (options.captureXmlHttpRequest !== false && Xhr && Xhr.prototype) {
    const states = new WeakMap();
    const prototype = Xhr.prototype;
    const originalOpen = prototype.open;
    const originalSend = prototype.send;
    const originalSetRequestHeader = prototype.setRequestHeader;

    function wrappedOpen(method, url) {
      states.set(this, {
        method: method || "GET",
        url: String(url),
        requestHeaders: [],
        suppressed: fetchInvocationDepth > 0,
      });
      return originalOpen.apply(this, arguments);
    }

    function wrappedSetRequestHeader(name, value) {
      const state = states.get(this);
      if (state) state.requestHeaders.push({ name, value });
      return originalSetRequestHeader.apply(this, arguments);
    }

    function wrappedSend() {
      const xhr = this;
      const state = states.get(xhr);
      if (!state || state.suppressed) return originalSend.apply(xhr, arguments);
      state.startedAtUtc = new Date().toISOString();
      state.started = monotonicNow(globalObject);
      state.requestBody = bodyFromValue(arguments[0], state.requestHeaders, options, globalObject);
      let failure;
      const markFailure = (event) => {
        failure = event && event.type ? event.type : "error";
      };
      const complete = () => {
        if (!active) return;
        let responseHeaders = [];
        try {
          responseHeaders = parseXhrResponseHeaders(xhr.getAllResponseHeaders());
        } catch (_) {
          // Some React Native XHR implementations throw before response headers exist.
        }
        let responseBody;
        try {
          const responseType = xhr.responseType || "text";
          if (responseType === "text" || responseType === "") {
            responseBody = bodyFromValue(xhr.responseText, responseHeaders, { ...options, captureRequestBody: options.captureResponseBody }, globalObject);
          } else if (responseType === "arraybuffer") {
            responseBody = bodyFromValue(xhr.response, responseHeaders, { ...options, captureRequestBody: options.captureResponseBody }, globalObject);
          }
        } catch (_) {
          // Response data is not readable for every XHR response type.
        }
        emitRecord(
          {
            source: `${sourcePrefix}.xhr`,
            startedAtUtc: state.startedAtUtc,
            completedAtUtc: new Date().toISOString(),
            durationMilliseconds: monotonicNow(globalObject) - state.started,
            method: state.method,
            url: xhr.responseURL || state.url,
            requestHeaders: state.requestHeaders,
            requestBodySizeBytes: parseContentLength(state.requestHeaders) ?? (state.requestBody && state.requestBody.totalBytes),
            requestBody: state.requestBody,
            statusCode: xhr.status || undefined,
            reasonPhrase: xhr.statusText,
            responseHeaders,
            responseBodySizeBytes: parseContentLength(responseHeaders) ?? (responseBody && responseBody.totalBytes),
            responseBody,
            errorType: failure,
            errorMessage: failure ? `XMLHttpRequest ${failure}` : undefined,
          },
          options,
          globalObject,
          capture,
        );
      };
      xhr.addEventListener("error", markFailure);
      xhr.addEventListener("abort", markFailure);
      xhr.addEventListener("timeout", markFailure);
      xhr.addEventListener("loadend", complete, { once: true });
      return originalSend.apply(xhr, arguments);
    }

    prototype.open = wrappedOpen;
    prototype.send = wrappedSend;
    prototype.setRequestHeader = wrappedSetRequestHeader;
    cleanups.push(() => {
      if (prototype.open === wrappedOpen) prototype.open = originalOpen;
      if (prototype.send === wrappedSend) prototype.send = originalSend;
      if (prototype.setRequestHeader === wrappedSetRequestHeader) {
        prototype.setRequestHeader = originalSetRequestHeader;
      }
    });
  }

  let removed = false;
  return {
    remove() {
      if (removed) return;
      removed = true;
      active = false;
      for (const cleanup of cleanups.reverse()) cleanup();
    },
  };
}

module.exports = {
  REDACTED_VALUE,
  SCHEMA,
  installNetworkCapture,
  sanitizeNetworkRequest,
};
