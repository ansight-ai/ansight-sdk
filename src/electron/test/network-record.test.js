"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { normalizeRequest } = require("../lib/network-record"),
  { createAnsight } = require("..");
test("manual network records redact credentials while retaining useful request evidence", () => {
  const r = normalizeRequest({
    url: "https://user:pass@example.test/items?page=2&token=private#secret",
    method: "post",
    statusCode: 201,
    requestHeaders: [
      { name: "Authorization", value: "Bearer private" },
      { name: "Content-Type", value: "application/json" },
    ],
    requestBody: {
      encoding: "utf8",
      data: JSON.stringify({
        password: "a quoted secret with spaces",
        count: 3,
        nested: { token: "private" },
      }),
    },
    performance: {
      dnsMilliseconds: 2,
      tlsMilliseconds: -1,
      reusedConnection: true,
    },
  });
  assert.equal(r.method, "POST");
  assert.equal(r.statusCode, 201);
  assert.ok(r.url.includes("page=2"));
  assert.ok(!JSON.stringify(r).includes("private"));
  assert.ok(!JSON.stringify(r).includes("quoted secret"));
  assert.equal(r.requestHeaders[0].value, "[REDACTED]");
  assert.equal(JSON.parse(r.requestBody.data).count, 3);
  assert.equal(
    r.requestBody.capturedBytes,
    Buffer.byteLength(r.requestBody.data),
  );
  assert.deepEqual(r.performance, {
    dnsMilliseconds: 2,
    reusedConnection: true,
  });
});
test("signed URL parameters and binary bodies cannot leak through default capture", () => {
  const r = normalizeRequest({
    url: "https://example.test/file?X-Amz-Signature=private&X-Amz-Credential=private&other=private",
    responseBody: {
      encoding: "base64",
      data: Buffer.from("private").toString("base64"),
    },
  });
  assert.ok(!JSON.stringify(r).includes("private"));
  assert.equal(r.responseBody, undefined);
  assert.equal(r.responseBodySizeBytes, 7);
  const raw = normalizeRequest(
    {
      url: "https://example.test?token=private",
      responseBody: { encoding: "base64", data: "YWJj" },
    },
    false,
  );
  assert.equal(raw.responseBody.data, "YWJj");
  assert.equal(raw.redactSensitiveData, false);
});
test("network payload bounds preserve valid UTF8 and reject malformed encodings", () => {
  const r = normalizeRequest({
    url: "https://example.test",
    responseBody: { encoding: "utf8", data: "😀".repeat(20000) },
  });
  assert.ok(r.responseBody.capturedBytes <= 65536);
  assert.equal(r.responseBody.truncated, true);
  assert.ok(!r.responseBody.data.includes("�"));
  assert.throws(
    () =>
      normalizeRequest({
        url: "https://example.test",
        responseBody: { encoding: "base64", data: "!" },
      }),
    /base64/,
  );
  assert.throws(
    () =>
      normalizeRequest({
        url: "https://example.test",
        requestBody: { encoding: "utf8", data: "a".repeat(2097153) },
      }),
    /2 MiB/,
  );
});
test("app sanitizer runs before persistence and can exclude requests or fail closed", () => {
  const queued = [];
  const sdk = createAnsight({
    appId: "test.network",
    enabled: true,
    electron: {},
    networkCapture: {
      sanitizer: (r) => ({ ...r, url: "https://example.test/redacted-path" }),
    },
  });
  sdk.offline = {
    append: (r) => {
      queued.push(r);
      return true;
    },
  };
  assert.equal(
    sdk.recordNetworkRequest({ url: "https://example.test/private-path" })
      .success,
    true,
  );
  assert.equal(queued[0].request.url, "https://example.test/redacted-path");
  sdk.options.networkCapture.sanitizer = () => null;
  sdk.recordNetworkRequest({ url: "https://example.test" });
  assert.equal(queued.length, 1);
  sdk.options.networkCapture.sanitizer = () => {
    throw new Error("scrubber failure");
  };
  assert.throws(
    () => sdk.recordNetworkRequest({ url: "https://example.test" }),
    /scrubber/,
  );
  assert.equal(queued.length, 1);
  sdk.options.networkCapture.sanitizer = undefined;
  sdk.setNetworkCaptureRedactionEnabled(false);
  sdk.recordNetworkRequest({ url: "https://example.test?token=explicit" });
  assert.ok(queued[1].request.url.includes("explicit"));
  sdk.options.enabled = false;
  assert.equal(
    sdk.recordNetworkRequest({ url: "https://example.test" }).success,
    false,
  );
  assert.equal(queued.length, 2);
});
