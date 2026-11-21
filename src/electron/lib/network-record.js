"use strict";
const { randomUUID } = require("node:crypto");
const sensitive =
  /^(access_token|accesskey|access_key|api_key|apikey|auth|authorization|client_secret|code|credential|credentials|id_token|jwt|key|password|passwd|refresh_token|sas|sastoken|secret|secret_key|security_token|session_token|sig|signature|token)$/i;
const text = (value, max) =>
  value == null ? undefined : String(value).slice(0, max);
const size = (value) =>
  Number.isSafeInteger(value) && value >= 0 ? value : undefined;
function sanitizeUrl(value) {
  try {
    const url = new URL(value);
    if (!["http:", "https:"].includes(url.protocol)) return "[unsupported URL]";
    url.username = "";
    url.password = "";
    url.hash = "";
    const names = [...url.searchParams.keys()];
    const signed = names.some((n) =>
      /^(sig|signature|x-amz-signature|x-goog-signature|x-oss-signature)$/i.test(
        n,
      ),
    );
    for (const name of names)
      if (sensitive.test(name) || signed)
        url.searchParams.set(name, "[REDACTED]");
    return url.toString().slice(0, 8192);
  } catch {
    return "[invalid URL]";
  }
}
function sanitizeText(value) {
  const original = String(value);
  // Structured JSON avoids leaking quoted passwords containing spaces or escapes.
  try {
    const parsed = JSON.parse(original);
    const visit = (v, depth = 0) => {
      if (depth > 64) return "[REDACTED: depth limit]";
      if (Array.isArray(v)) return v.map((x) => visit(x, depth + 1));
      if (v && typeof v === "object")
        return Object.fromEntries(
          Object.entries(v).map(([k, x]) => [
            k,
            sensitive.test(k) ? "[REDACTED]" : visit(x, depth + 1),
          ]),
        );
      return typeof v === "string" ? redact(v) : v;
    };
    return JSON.stringify(visit(parsed));
  } catch {
    return redact(original);
  }
}
function redact(value) {
  return value
    .replace(/https?:\/\/[^\s"'<>]+/gi, sanitizeUrl)
    .replace(
      /\b(access_token|accesskey|access_key|api_key|apikey|auth|authorization|client_secret|code|credential|credentials|id_token|jwt|key|password|passwd|refresh_token|sas|sastoken|secret|secret_key|security_token|session_token|sig|signature|token)(["']?\s*[:=]\s*)("(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[^&\s,;}]+)/gi,
      "$1$2[REDACTED]",
    );
}
function normalizeRequest(input, redaction = true) {
  if (!input || typeof input !== "object")
    throw new Error("Network request must be an object");
  const timestamp = (v, fallback) =>
    Number.isFinite(Date.parse(v)) ? new Date(v).toISOString() : fallback;
  const started = timestamp(input.startedAtUtc, new Date().toISOString());
  const completed = timestamp(input.completedAtUtc, started);
  const result = {
    schema: "ansight.network-request.v1",
    id: text(input.id, 128) || randomUUID(),
    source: text(input.source, 128) || "electron.app",
    startedAtUtc: started,
    completedAtUtc: completed < started ? started : completed,
    durationMilliseconds: Number.isFinite(input.durationMilliseconds)
      ? Math.max(0, input.durationMilliseconds)
      : Math.max(0, Date.parse(completed) - Date.parse(started)),
    method: (text(input.method, 32) || "GET").toUpperCase(),
    url: redaction
      ? sanitizeUrl(input.url)
      : text(input.url, 8192) || "[unknown]",
    redactSensitiveData: redaction,
  };
  for (const key of ["protocol", "reasonPhrase", "errorType", "errorMessage"]) {
    const value = text(input[key], key === "errorMessage" ? 2048 : 512);
    if (value != null) result[key] = redaction ? sanitizeText(value) : value;
  }
  if (
    Number.isInteger(input.statusCode) &&
    input.statusCode >= 100 &&
    input.statusCode <= 999
  )
    result.statusCode = input.statusCode;
  for (const prefix of ["request", "response"]) {
    const headers = input[prefix + "Headers"] ?? [];
    if (!Array.isArray(headers))
      throw new Error("Network headers must be an array");
    result[prefix + "Headers"] = headers
      .slice(0, 100)
      .filter((h) => h && typeof h.name === "string" && h.name.trim())
      .map((h) => ({
        name: text(h.name, 256),
        value:
          redaction &&
          /authorization|cookie|token|secret|api[-_]?key/i.test(h.name)
            ? "[REDACTED]"
            : text(h.value, 4096) || "",
      }));
    result[prefix + "BodySizeBytes"] = size(input[prefix + "BodySizeBytes"]);
    const body = input[prefix + "Body"];
    if (body != null) {
      if (
        !["utf8", "base64"].includes(body.encoding) ||
        typeof body.data !== "string"
      )
        throw new Error("Body must contain utf8 or base64 data");
      if (Buffer.byteLength(body.data) > 2 * 1024 * 1024)
        throw new Error("Network body input exceeds 2 MiB");
      if (
        body.encoding === "base64" &&
        !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(
          body.data,
        )
      )
        throw new Error("Invalid base64 body");
      let data = body.data;
      if (redaction) {
        // Binary data cannot be reliably scrubbed. Retain its byte measurements, not its payload.
        if (body.encoding === "base64") {
          result[prefix + "BodySizeBytes"] ??= Buffer.from(
            data,
            "base64",
          ).length;
          continue;
        }
        data = sanitizeText(data);
      }
      const bytes = Buffer.from(
        data,
        body.encoding === "utf8" ? "utf8" : "base64",
      );
      let captured = bytes.subarray(0, 65536);
      if (body.encoding === "utf8") {
        let end = captured.length;
        while (
          end &&
          (() => {
            try {
              new TextDecoder("utf-8", { fatal: true }).decode(
                captured.subarray(0, end),
              );
              return false;
            } catch {
              return true;
            }
          })()
        )
          end--;
        captured = captured.subarray(0, end);
      }
      const totalBytes = size(body.totalBytes);
      result[prefix + "Body"] = {
        contentType: text(body.contentType, 512),
        encoding: body.encoding,
        data: captured.toString(body.encoding === "utf8" ? "utf8" : "base64"),
        capturedBytes: captured.length,
        totalBytes,
        truncated:
          body.truncated === true ||
          bytes.length > captured.length ||
          (totalBytes != null && totalBytes > captured.length),
      };
    }
  }
  if (input.performance && typeof input.performance === "object") {
    result.performance = {};
    for (const key of [
      "timeToFirstByteMilliseconds",
      "dnsMilliseconds",
      "connectMilliseconds",
      "tlsMilliseconds",
      "uploadDurationMilliseconds",
      "downloadDurationMilliseconds",
      "uploadedBytes",
      "downloadedBytes",
    ])
      if (
        Number.isFinite(input.performance[key]) &&
        input.performance[key] >= 0
      )
        result.performance[key] = input.performance[key];
    if (typeof input.performance.reusedConnection === "boolean")
      result.performance.reusedConnection = input.performance.reusedConnection;
  }
  return result;
}
module.exports = { normalizeRequest, sanitizeUrl, sanitizeText };
