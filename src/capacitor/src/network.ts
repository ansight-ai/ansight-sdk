import type {
  AnsightNetworkCaptureOptions,
  AnsightNetworkBody,
  AnsightNetworkHeader,
  AnsightNetworkRequest,
  AnsightNetworkRequestInput,
  AnsightNetworkSanitizationOptions,
  AnsightSubscription,
} from "./definitions";

export const networkRequestSchema = "ansight.network-request.v1" as const;
export const redactedNetworkValue = "<redacted>";

const maximumHeaderCount = 128;
const maximumHeaderValueLength = 4096;
const maximumErrorMessageLength = 4096;
const maximumUrlLength = 16384;
const defaultMaximumBodyBytes = 64 * 1024;
const sensitiveHeaderNames = new Set([
  "authorization",
  "cookie",
  "proxy-authorization",
  "set-cookie",
  "x-api-key",
  "x-auth-token",
]);
const sensitiveQueryNames = new Set([
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
const azureSasFingerprintNames = new Set([
  "se",
  "skoid",
  "sp",
  "sr",
  "srt",
  "ss",
  "sv",
]);
const azureSasQueryNames = new Set([
  "epk",
  "erk",
  "rscc",
  "rscd",
  "rsce",
  "rscl",
  "rsct",
  "saoid",
  "scid",
  "se",
  "sig",
  "si",
  "sip",
  "ske",
  "skoid",
  "sks",
  "skt",
  "sktid",
  "skv",
  "snapshot",
  "sp",
  "spk",
  "spr",
  "sr",
  "srk",
  "srt",
  "ss",
  "st",
  "suoid",
  "tn",
  "versionid",
  "sv",
]);

function truncate(value: unknown, maximumLength: number): string {
  const text = String(value);
  return text.length <= maximumLength
    ? text
    : `${text.slice(0, maximumLength)}…`;
}

function normalizeRequired(
  value: unknown,
  fallback: string,
  maximumLength: number,
): string {
  const normalized = value == null ? "" : String(value).trim();
  return truncate(normalized || fallback, maximumLength);
}

function normalizeOptional(
  value: unknown,
  maximumLength: number,
): string | undefined {
  if (value == null) return undefined;
  const normalized = String(value).trim();
  return normalized ? truncate(normalized, maximumLength) : undefined;
}

function lowercaseSet(values: readonly string[] | undefined): Set<string> {
  return new Set((values ?? []).map((value) => value.toLowerCase()));
}

function isSensitiveHeader(
  name: string,
  options: AnsightNetworkSanitizationOptions,
): boolean {
  const lowered = name.toLowerCase();
  if (
    sensitiveHeaderNames.has(lowered) ||
    lowercaseSet(options.additionalSensitiveHeaderNames).has(lowered)
  ) {
    return true;
  }
  const compact = lowered.replaceAll("-", "");
  return (
    compact.includes("token") ||
    compact.includes("secret") ||
    compact.includes("apikey")
  );
}

function headerEntries(headers: unknown): Array<[unknown, unknown]> {
  if (!headers) return [];
  if (Array.isArray(headers)) {
    return headers.flatMap((header): Array<[unknown, unknown]> => {
      if (Array.isArray(header)) return [[header[0], header[1]]];
      if (header && typeof header === "object") {
        const value = header as { name?: unknown; value?: unknown };
        return [[value.name, value.value]];
      }
      return [];
    });
  }
  if (typeof (headers as Headers).forEach === "function") {
    const entries: Array<[unknown, unknown]> = [];
    (headers as Headers).forEach((value, name) => entries.push([name, value]));
    return entries;
  }
  return typeof headers === "object" ? Object.entries(headers) : [];
}

function sanitizeHeaders(
  headers: unknown,
  options: AnsightNetworkSanitizationOptions,
): AnsightNetworkHeader[] {
  return headerEntries(headers)
    .filter(([name]) => name != null && String(name).trim())
    .slice(0, maximumHeaderCount)
    .map(([rawName, rawValue]) => {
      const name = normalizeRequired(rawName, "Header", 256);
      return {
        name,
        value: isSensitiveHeader(name, options)
          ? redactedNetworkValue
          : normalizeRequired(rawValue, "", maximumHeaderValueLength),
      };
    });
}

function sanitizeQuery(
  query: string,
  options: AnsightNetworkSanitizationOptions,
): string {
  const appSensitive = lowercaseSet(
    options.additionalSensitiveQueryParameterNames,
  );
  const pairs = query.split("&");
  const decodedNames = new Set(
    pairs.map((pair) => decodeQueryName(pair).toLowerCase()),
  );
  const hasAzureSas =
    decodedNames.has("sig") &&
    [...azureSasFingerprintNames].some((name) => decodedNames.has(name));
  const hasAwsSignature = decodedNames.has("x-amz-signature");
  const hasGoogleSignature = decodedNames.has("x-goog-signature");
  const hasCloudFrontSignature =
    decodedNames.has("signature") &&
    ["key-pair-id", "policy", "expires"].some((name) => decodedNames.has(name));
  const hasLegacyGoogleSignature =
    decodedNames.has("signature") && decodedNames.has("googleaccessid");
  const hasAlibabaSignature =
    (decodedNames.has("signature") && decodedNames.has("ossaccesskeyid")) ||
    decodedNames.has("x-oss-signature");
  return pairs
    .map((pair) => {
      const equalsIndex = pair.indexOf("=");
      const encodedName = equalsIndex < 0 ? pair : pair.slice(0, equalsIndex);
      const decodedName = decodeQueryName(pair);
      const lowered = decodedName.toLowerCase();
      const providerSensitive =
        (hasAzureSas && azureSasQueryNames.has(lowered)) ||
        (hasAwsSignature && lowered.startsWith("x-amz-")) ||
        (hasGoogleSignature && lowered.startsWith("x-goog-")) ||
        (hasCloudFrontSignature &&
          [
            "signature",
            "key-pair-id",
            "policy",
            "expires",
            "hash-algorithm",
          ].includes(lowered)) ||
        (hasLegacyGoogleSignature &&
          ["signature", "googleaccessid", "expires"].includes(lowered)) ||
        (hasAlibabaSignature &&
          (lowered.startsWith("x-oss-") ||
            ["signature", "ossaccesskeyid", "security-token"].includes(
              lowered,
            )));
      return providerSensitive ||
        sensitiveQueryNames.has(lowered) ||
        appSensitive.has(lowered)
        ? `${encodedName}=${encodeURIComponent(redactedNetworkValue)}`
        : pair;
    })
    .join("&");
}

function decodeQueryName(pair: string): string {
  const equalsIndex = pair.indexOf("=");
  const encodedName = equalsIndex < 0 ? pair : pair.slice(0, equalsIndex);
  try {
    return decodeURIComponent(encodedName.replaceAll("+", " "));
  } catch {
    return encodedName;
  }
}

function sanitizeUrl(
  value: unknown,
  options: AnsightNetworkSanitizationOptions,
): string {
  let normalized = normalizeRequired(value, "<unknown>", maximumUrlLength);
  normalized = normalized.replace(
    /^(https?:\/\/)[^/@]+@/i,
    `$1${redactedNetworkValue}@`,
  );
  const queryIndex = normalized.indexOf("?");
  if (queryIndex < 0) return truncate(normalized, maximumUrlLength);
  const fragmentIndex = normalized.indexOf("#", queryIndex);
  if (options.includeQueryString === false) {
    return truncate(
      normalized.slice(0, queryIndex) +
        (fragmentIndex < 0 ? "" : normalized.slice(fragmentIndex)),
      maximumUrlLength,
    );
  }
  const queryEnd = fragmentIndex < 0 ? normalized.length : fragmentIndex;
  return truncate(
    normalized.slice(0, queryIndex + 1) +
      sanitizeQuery(normalized.slice(queryIndex + 1, queryEnd), options) +
      (fragmentIndex < 0 ? "" : normalized.slice(fragmentIndex)),
    maximumUrlLength,
  );
}

function sanitizeErrorMessage(
  value: unknown,
  options: AnsightNetworkSanitizationOptions,
): string | undefined {
  const normalized = normalizeOptional(value, maximumErrorMessageLength);
  if (!normalized) return undefined;
  return truncate(
    normalized
      .replace(
        /(access_token|api_key|apikey|auth|authorization|code|key|password|passwd|secret|signature|token)(\s*=\s*)([^&\s,;]+)/gi,
        `$1$2${redactedNetworkValue}`,
      )
      .replace(/https?:\/\/[^\s"'<>]+/gi, (url) => sanitizeUrl(url, options)),
    maximumErrorMessageLength,
  );
}

function normalizeTimestamp(value: unknown, fallback: string): string {
  const date = new Date(value == null ? fallback : String(value));
  return Number.isFinite(date.valueOf()) ? date.toISOString() : fallback;
}

function generateId(globalObject: typeof globalThis): string {
  if (typeof globalObject.crypto?.randomUUID === "function") {
    return globalObject.crypto.randomUUID().replaceAll("-", "");
  }
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2)}`;
}

function normalizeSize(value: unknown): number | undefined {
  const number = Number(value);
  return Number.isFinite(number) && number >= 0
    ? Math.round(number)
    : undefined;
}

function maximumBodyBytes(options: AnsightNetworkSanitizationOptions): number {
  const configured = Number(options.maximumBodyBytes);
  const value = Number.isFinite(configured)
    ? Math.round(configured)
    : defaultMaximumBodyBytes;
  return Math.max(0, value);
}

function sanitizeSensitiveText(
  value: string,
  options: AnsightNetworkSanitizationOptions,
): string {
  return value
    .replace(
      /(access_token|accesskey|access_key|api_key|apikey|auth|authorization|client_secret|code|credential|credentials|id_token|jwt|key|password|passwd|refresh_token|sas|sastoken|secret|secret_key|security_token|session_token|sig|signature|token)(["']?\s*[:=]\s*["']?)([^&\s,;}"']+)/gi,
      `$1$2${redactedNetworkValue}`,
    )
    .replace(/https?:\/\/[^\s"'<>]+/gi, (url) => sanitizeUrl(url, options));
}

function truncateUtf8(bytes: Uint8Array, maximum: number): Uint8Array {
  let length = Math.min(bytes.length, maximum);
  const decoder = new TextDecoder("utf-8", { fatal: true });
  while (length > 0) {
    try {
      decoder.decode(bytes.slice(0, length));
      return bytes.slice(0, length);
    } catch {
      length -= 1;
    }
  }
  return new Uint8Array();
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary);
}

function base64ToBytes(value: string): Uint8Array {
  const binary = atob(value);
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

function normalizeBody(
  body: AnsightNetworkBody | undefined,
  options: AnsightNetworkSanitizationOptions,
): AnsightNetworkBody | undefined {
  const maximum = maximumBodyBytes(options);
  if (!body || maximum <= 0) return undefined;
  const encoding = body.encoding?.toLowerCase();
  let bytes: Uint8Array;
  try {
    if (encoding === "utf8") {
      bytes = new TextEncoder().encode(
        sanitizeSensitiveText(body.data, options),
      );
    } else if (encoding === "base64" && options.captureBinaryBodies === true) {
      bytes = base64ToBytes(body.data);
    } else {
      return undefined;
    }
  } catch {
    return undefined;
  }
  const originalLength = bytes.length;
  const captured =
    encoding === "utf8"
      ? truncateUtf8(bytes, maximum)
      : bytes.slice(0, maximum);
  const totalBytes = normalizeSize(body.totalBytes);
  return {
    contentType: normalizeOptional(body.contentType, 512),
    encoding,
    data:
      encoding === "base64"
        ? bytesToBase64(captured)
        : new TextDecoder().decode(captured),
    capturedBytes: captured.length,
    totalBytes,
    truncated:
      body.truncated ||
      originalLength > captured.length ||
      (totalBytes != null && totalBytes > captured.length),
  };
}

function normalizeRecord(
  input: Partial<AnsightNetworkRequest>,
  options: AnsightNetworkSanitizationOptions,
  globalObject: typeof globalThis,
): AnsightNetworkRequest {
  const now = new Date().toISOString();
  const startedAtUtc = normalizeTimestamp(input.startedAtUtc, now);
  const completedAtUtc = normalizeTimestamp(input.completedAtUtc, startedAtUtc);
  const duration = Number(input.durationMilliseconds);
  return {
    schema: networkRequestSchema,
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
    requestBody:
      options.captureRequestBody !== false
        ? normalizeBody(input.requestBody, options)
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
    responseBody:
      options.captureResponseBody !== false
        ? normalizeBody(input.responseBody, options)
        : undefined,
    errorType: normalizeOptional(input.errorType, 512),
    errorMessage: sanitizeErrorMessage(input.errorMessage, options),
  };
}

export function sanitizeNetworkRequest(
  input: AnsightNetworkRequestInput,
  options: AnsightNetworkSanitizationOptions = {},
  globalObject: typeof globalThis = globalThis,
): AnsightNetworkRequest | null {
  try {
    let normalized = normalizeRecord(input, options, globalObject);
    if (options.urlSanitizer) {
      normalized = normalizeRecord(
        { ...normalized, url: options.urlSanitizer(normalized.url) },
        options,
        globalObject,
      );
    }
    if (options.requestSanitizer) {
      const transformed = options.requestSanitizer(normalized);
      if (transformed == null) return null;
      normalized = normalizeRecord(transformed, options, globalObject);
    }
    return normalized;
  } catch {
    return null;
  }
}

function parseContentLength(headers: unknown): number | undefined {
  const entry = headerEntries(headers).find(
    ([name]) => String(name).toLowerCase() === "content-length",
  );
  return entry ? normalizeSize(entry[1]) : undefined;
}

function headerValue(headers: unknown, wantedName: string): string | undefined {
  const entry = headerEntries(headers).find(
    ([name]) => String(name).toLowerCase() === wantedName,
  );
  return entry == null ? undefined : String(entry[1]);
}

function isTextContentType(contentType: string | undefined): boolean {
  if (!contentType) return true;
  const mediaType = contentType.split(";", 1)[0].trim().toLowerCase();
  return (
    mediaType.startsWith("text/") ||
    mediaType.endsWith("+json") ||
    mediaType.endsWith("+xml") ||
    [
      "application/json",
      "application/xml",
      "application/graphql",
      "application/javascript",
      "application/x-www-form-urlencoded",
    ].includes(mediaType)
  );
}

function bodyFromBytes(
  bytes: Uint8Array,
  totalBytes: number | undefined,
  contentType: string | undefined,
  options: AnsightNetworkSanitizationOptions,
): AnsightNetworkBody | undefined {
  const binary = !isTextContentType(contentType);
  if (binary && options.captureBinaryBodies !== true) return undefined;
  const maximum = maximumBodyBytes(options);
  if (maximum <= 0) return undefined;
  const captured = binary
    ? bytes.slice(0, maximum)
    : truncateUtf8(bytes, maximum);
  return {
    contentType,
    encoding: binary ? "base64" : "utf8",
    data: binary ? bytesToBase64(captured) : new TextDecoder().decode(captured),
    capturedBytes: captured.length,
    totalBytes,
    truncated:
      bytes.length > captured.length ||
      (totalBytes != null && totalBytes > captured.length),
  };
}

function bodyFromValue(
  value: unknown,
  headers: unknown,
  options: AnsightNetworkSanitizationOptions,
): AnsightNetworkBody | undefined {
  if (value == null || options.captureRequestBody === false) return undefined;
  const contentType = headerValue(headers, "content-type");
  if (typeof value === "string" || value instanceof URLSearchParams) {
    const bytes = new TextEncoder().encode(String(value));
    return bodyFromBytes(
      bytes,
      bytes.length,
      contentType ||
        (value instanceof URLSearchParams
          ? "application/x-www-form-urlencoded"
          : undefined),
      options,
    );
  }
  if (value instanceof ArrayBuffer) {
    const bytes = new Uint8Array(value);
    return bodyFromBytes(bytes, bytes.length, contentType, options);
  }
  if (ArrayBuffer.isView(value)) {
    const bytes = new Uint8Array(
      value.buffer,
      value.byteOffset,
      value.byteLength,
    );
    return bodyFromBytes(bytes, bytes.length, contentType, options);
  }
  return undefined;
}

async function bodyFromFetchResponse(
  response: Response,
  headers: unknown,
  options: AnsightNetworkSanitizationOptions,
  shouldContinue: () => boolean = () => true,
): Promise<AnsightNetworkBody | undefined> {
  if (!shouldContinue() || options.captureResponseBody === false)
    return undefined;
  const contentType = headerValue(headers, "content-type");
  if (!isTextContentType(contentType) && options.captureBinaryBodies !== true) {
    return undefined;
  }
  const totalBytes = parseContentLength(headers);
  const maximum = maximumBodyBytes(options);
  if (maximum <= 0) return undefined;
  const clone = response.clone();
  if (clone.body) {
    const reader = clone.body.getReader();
    const chunks: Uint8Array[] = [];
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
        const chunk = result.value;
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
      reader.releaseLock();
    }
    const joined = new Uint8Array(capturedLength);
    let offset = 0;
    for (const chunk of chunks) {
      joined.set(chunk, offset);
      offset += chunk.length;
    }
    return bodyFromBytes(
      joined,
      totalBytes ?? observedLength,
      contentType,
      options,
    );
  }
  if (totalBytes == null || totalBytes > maximum) return undefined;
  const bytes = new Uint8Array(await clone.arrayBuffer());
  if (!shouldContinue()) return undefined;
  return bodyFromBytes(bytes, totalBytes, contentType, options);
}

function parseXhrResponseHeaders(value: string): AnsightNetworkHeader[] {
  return value
    .trim()
    .split(/[\r\n]+/)
    .flatMap((line) => {
      const separator = line.indexOf(":");
      return separator < 0
        ? []
        : [
            {
              name: line.slice(0, separator),
              value: line.slice(separator + 1),
            },
          ];
    });
}

function monotonicNow(globalObject: typeof globalThis): number {
  return typeof globalObject.performance?.now === "function"
    ? globalObject.performance.now()
    : Date.now();
}

export function installBrowserNetworkCapture(
  capture: (request: AnsightNetworkRequest) => unknown,
  options: AnsightNetworkCaptureOptions = {},
  sourcePrefix = "capacitor",
  globalObject: typeof globalThis = globalThis,
): AnsightSubscription {
  const cleanups: Array<() => void> = [];
  let active = true;
  let fetchInvocationDepth = 0;

  if (
    options.captureFetch !== false &&
    typeof globalObject.fetch === "function"
  ) {
    const originalFetch = globalObject.fetch;
    const wrappedFetch: typeof fetch = function (input, init) {
      const startedAtUtc = new Date().toISOString();
      const started = monotonicNow(globalObject);
      const inputRequest =
        typeof input === "object" && "headers" in input && "method" in input
          ? (input as Request)
          : undefined;
      const requestHeaders = headerEntries(inputRequest?.headers).concat(
        headerEntries(init?.headers),
      );
      const requestBody = bodyFromValue(init?.body, requestHeaders, options);
      const method = init?.method || inputRequest?.method || "GET";
      const url =
        typeof input === "string" ? input : inputRequest?.url || String(input);
      let promise: Promise<Response>;
      fetchInvocationDepth += 1;
      try {
        promise = originalFetch(input, init);
      } catch (error) {
        fetchInvocationDepth -= 1;
        const request = sanitizeNetworkRequest(
          {
            source: `${sourcePrefix}.fetch`,
            startedAtUtc,
            completedAtUtc: new Date().toISOString(),
            durationMilliseconds: monotonicNow(globalObject) - started,
            method,
            url,
            requestHeaders: sanitizeHeaders(requestHeaders, {}),
            requestBodySizeBytes:
              parseContentLength(requestHeaders) ?? requestBody?.totalBytes,
            requestBody,
            errorType: error instanceof Error ? error.name : "Error",
            errorMessage:
              error instanceof Error ? error.message : String(error),
          },
          options,
          globalObject,
        );
        if (active && request)
          Promise.resolve(capture(request)).catch(() => undefined);
        throw error;
      }
      fetchInvocationDepth -= 1;
      return promise.then(
        (response) => {
          if (!active) return response;
          const responseHeaders = headerEntries(response.headers);
          const responseRecord: AnsightNetworkRequestInput = {
            source: `${sourcePrefix}.fetch`,
            startedAtUtc,
            completedAtUtc: new Date().toISOString(),
            durationMilliseconds: monotonicNow(globalObject) - started,
            method,
            url: response.url || url,
            requestHeaders: sanitizeHeaders(requestHeaders, {}),
            requestBodySizeBytes:
              parseContentLength(requestHeaders) ?? requestBody?.totalBytes,
            requestBody,
            statusCode: response.status,
            reasonPhrase: response.statusText,
            responseHeaders: sanitizeHeaders(responseHeaders, {}),
            responseBodySizeBytes: parseContentLength(responseHeaders),
          };
          return bodyFromFetchResponse(
            response,
            responseHeaders,
            options,
            () => active,
          ).then(
            (responseBody) => {
              const request = sanitizeNetworkRequest(
                {
                  ...responseRecord,
                  responseBody,
                  responseBodySizeBytes:
                    responseRecord.responseBodySizeBytes ??
                    responseBody?.totalBytes,
                },
                options,
                globalObject,
              );
              if (active && request)
                void Promise.resolve(capture(request)).catch(() => undefined);
              return response;
            },
            () => {
              const request = sanitizeNetworkRequest(
                responseRecord,
                options,
                globalObject,
              );
              if (active && request)
                void Promise.resolve(capture(request)).catch(() => undefined);
              return response;
            },
          );
        },
        (error: unknown) => {
          const request = sanitizeNetworkRequest(
            {
              source: `${sourcePrefix}.fetch`,
              startedAtUtc,
              completedAtUtc: new Date().toISOString(),
              durationMilliseconds: monotonicNow(globalObject) - started,
              method,
              url,
              requestHeaders: sanitizeHeaders(requestHeaders, {}),
              requestBodySizeBytes:
                parseContentLength(requestHeaders) ?? requestBody?.totalBytes,
              requestBody,
              errorType: error instanceof Error ? error.name : "Error",
              errorMessage:
                error instanceof Error ? error.message : String(error),
            },
            options,
            globalObject,
          );
          if (active && request)
            Promise.resolve(capture(request)).catch(() => undefined);
          throw error;
        },
      );
    };
    globalObject.fetch = wrappedFetch;
    cleanups.push(() => {
      if (globalObject.fetch === wrappedFetch)
        globalObject.fetch = originalFetch;
    });
  }

  const Xhr = globalObject.XMLHttpRequest;
  if (options.captureXmlHttpRequest !== false && Xhr?.prototype) {
    type State = {
      method: string;
      url: string;
      requestHeaders: AnsightNetworkHeader[];
      suppressed: boolean;
      startedAtUtc?: string;
      started?: number;
      requestBody?: AnsightNetworkBody;
    };
    const states = new WeakMap<XMLHttpRequest, State>();
    const prototype = Xhr.prototype;
    const originalOpen = prototype.open;
    const originalSend = prototype.send;
    const originalSetRequestHeader = prototype.setRequestHeader;

    const wrappedOpen = function (
      this: XMLHttpRequest,
      method: string,
      url: string | URL,
      ...rest: unknown[]
    ): void {
      states.set(this, {
        method,
        url: String(url),
        requestHeaders: [],
        suppressed: fetchInvocationDepth > 0,
      });
      Reflect.apply(originalOpen, this, [method, url, ...rest]);
    } as XMLHttpRequest["open"];
    const wrappedSetRequestHeader = function (
      this: XMLHttpRequest,
      name: string,
      value: string,
    ): void {
      states.get(this)?.requestHeaders.push({ name, value });
      Reflect.apply(originalSetRequestHeader, this, [name, value]);
    };
    const wrappedSend = function (
      this: XMLHttpRequest,
      body?: Document | XMLHttpRequestBodyInit | null,
    ): void {
      const state = states.get(this);
      if (!state || state.suppressed) {
        Reflect.apply(originalSend, this, [body]);
        return;
      }
      state.startedAtUtc = new Date().toISOString();
      state.started = monotonicNow(globalObject);
      state.requestBody = bodyFromValue(body, state.requestHeaders, options);
      let failure: string | undefined;
      const markFailure = (event: Event) => {
        failure = event.type;
      };
      const complete = () => {
        if (!active) return;
        let responseHeaders: AnsightNetworkHeader[] = [];
        try {
          responseHeaders = parseXhrResponseHeaders(
            this.getAllResponseHeaders(),
          );
        } catch {
          // Some WebViews throw before response headers exist.
        }
        let responseBody: AnsightNetworkBody | undefined;
        try {
          const responseType = this.responseType || "text";
          const responseOptions = {
            ...options,
            captureRequestBody: options.captureResponseBody,
          };
          if (responseType === "text") {
            responseBody = bodyFromValue(
              this.responseText,
              responseHeaders,
              responseOptions,
            );
          } else if (responseType === "arraybuffer") {
            responseBody = bodyFromValue(
              this.response as ArrayBuffer,
              responseHeaders,
              responseOptions,
            );
          }
        } catch {
          // Response data is not readable for every XHR response type.
        }
        const request = sanitizeNetworkRequest(
          {
            source: `${sourcePrefix}.xhr`,
            startedAtUtc: state.startedAtUtc,
            completedAtUtc: new Date().toISOString(),
            durationMilliseconds:
              monotonicNow(globalObject) - (state.started ?? 0),
            method: state.method,
            url: this.responseURL || state.url,
            requestHeaders: state.requestHeaders,
            requestBodySizeBytes:
              parseContentLength(state.requestHeaders) ??
              state.requestBody?.totalBytes,
            requestBody: state.requestBody,
            statusCode: this.status || undefined,
            reasonPhrase: this.statusText,
            responseHeaders,
            responseBodySizeBytes:
              parseContentLength(responseHeaders) ?? responseBody?.totalBytes,
            responseBody,
            errorType: failure,
            errorMessage: failure ? `XMLHttpRequest ${failure}` : undefined,
          },
          options,
          globalObject,
        );
        if (request) Promise.resolve(capture(request)).catch(() => undefined);
      };
      this.addEventListener("error", markFailure);
      this.addEventListener("abort", markFailure);
      this.addEventListener("timeout", markFailure);
      this.addEventListener("loadend", complete, { once: true });
      Reflect.apply(originalSend, this, [body]);
    };

    prototype.open = wrappedOpen;
    prototype.setRequestHeader = wrappedSetRequestHeader;
    prototype.send = wrappedSend;
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
