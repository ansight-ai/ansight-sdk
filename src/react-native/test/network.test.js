"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");
const {
  installNetworkCapture,
  sanitizeNetworkRequest,
} = require("../network");

test("mandatory sanitizer redacts credentials after the app callback", () => {
  const request = sanitizeNetworkRequest(
    {
      id: "request-1",
      source: "test",
      startedAtUtc: "2026-08-23T00:00:00.000Z",
      completedAtUtc: "2026-08-23T00:00:00.010Z",
      durationMilliseconds: 10,
      method: "get",
      url: "https://user:password@example.test/items?token=secret&visible=yes",
      requestHeaders: [{ name: "Authorization", value: "Bearer first" }],
    },
    {
      requestSanitizer(value) {
        return {
          ...value,
          requestHeaders: [{ name: "Authorization", value: "Bearer restored" }],
        };
      },
    },
    {},
  );

  assert.equal(request.method, "GET");
  assert.match(request.url, /token=%3Credacted%3E/);
  assert.doesNotMatch(request.url, /password|secret/);
  assert.equal(request.requestHeaders[0].value, "<redacted>");
});

test("sanitizer redacts cloud signed URLs and text bodies", () => {
  for (const url of [
    "https://blob.test/a?sv=1&sp=rw&se=tomorrow&sig=azure-secret&safe=yes",
    "https://s3.test/a?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=credential-secret&X-Amz-Signature=aws-secret&safe=yes",
    "https://storage.test/a?X-Goog-Algorithm=GOOG4-RSA-SHA256&X-Goog-Credential=google-secret&X-Goog-Signature=gcs-secret&safe=yes",
  ]) {
    const request = sanitizeNetworkRequest({
      method: "POST",
      url,
      requestBody: {
        contentType: "application/json",
        encoding: "utf8",
        data: '{"token":"body-secret","visible":"yes"}',
        capturedBytes: 39,
        totalBytes: 39,
        truncated: false,
      },
    }, {}, {});
    assert.doesNotMatch(request.url, /azure-secret|credential-secret|aws-secret|google-secret|gcs-secret/);
    assert.match(request.url, /safe=yes/);
    assert.doesNotMatch(request.requestBody.data, /body-secret/);
    assert.match(request.requestBody.data, /visible/);
  }
});

test("fetch integration captures bounded request bodies without consuming responses", async () => {
  const captures = [];
  const response = {
    status: 201,
    statusText: "Created",
    url: "https://example.test/items?key=secret",
    headers: { "content-length": "42", "set-cookie": "session=secret" },
    body: { untouched: true },
  };
  const runtime = {
    fetch: async () => response,
    performance: { now: (() => { let value = 0; return () => ++value; })() },
  };
  const subscription = installNetworkCapture({
    globalObject: runtime,
    sourcePrefix: "react-native",
    capture: (request) => captures.push(request),
  });

  const actual = await runtime.fetch("https://example.test/items?token=secret", {
    method: "post",
    headers: { Authorization: "Bearer secret" },
    body: "token=request-secret&visible=yes",
  });
  subscription.remove();

  assert.equal(actual, response);
  assert.equal(captures.length, 1);
  assert.equal(captures[0].source, "react-native.fetch");
  assert.equal(captures[0].method, "POST");
  assert.equal(captures[0].statusCode, 201);
  assert.equal(captures[0].responseBodySizeBytes, 42);
  assert.equal(captures[0].requestHeaders[0].value, "<redacted>");
  assert.equal(captures[0].responseHeaders[1].value, "<redacted>");
  assert.equal(captures[0].requestBody.encoding, "utf8");
  assert.doesNotMatch(captures[0].requestBody.data, /request-secret/);
  assert.match(captures[0].requestBody.data, /visible=yes/);
  assert.equal(response.body.untouched, true);
});

test("request and response bodies can be excluded independently", () => {
  const request = sanitizeNetworkRequest({
    method: "POST",
    url: "https://example.test",
    requestBody: { encoding: "utf8", data: "request", capturedBytes: 7, truncated: false },
    responseBody: { encoding: "utf8", data: "response", capturedBytes: 8, truncated: false },
  }, { captureRequestBody: false }, {});
  assert.equal(request.requestBody, undefined);
  assert.equal(request.responseBody.data, "response");
});

test("fetch integration records synchronous transport failures", () => {
  const captures = [];
  const failure = new TypeError("token=secret");
  const runtime = {
    fetch() {
      throw failure;
    },
    performance: { now: () => 10 },
  };
  const subscription = installNetworkCapture({
    globalObject: runtime,
    capture: (request) => captures.push(request),
  });

  assert.throws(() => runtime.fetch("https://example.test?token=secret"), failure);
  subscription.remove();

  assert.equal(captures.length, 1);
  assert.equal(captures[0].errorType, "TypeError");
  assert.equal(captures[0].errorMessage, "token=<redacted>");
});

test("removing capture ignores an in-flight response before reading its body", async () => {
  const captures = [];
  let resolveFetch;
  let cloneCalls = 0;
  const response = {
    status: 200,
    statusText: "OK",
    url: "https://example.test/items",
    headers: { "content-type": "text/plain", "content-length": "7" },
    clone() {
      cloneCalls += 1;
      throw new Error("body capture should not start");
    },
  };
  const runtime = {
    fetch: () => new Promise((resolve) => { resolveFetch = resolve; }),
    performance: { now: () => 10 },
  };
  const subscription = installNetworkCapture({
    globalObject: runtime,
    capture: (request) => captures.push(request),
  });

  const pending = runtime.fetch("https://example.test/items");
  subscription.remove();
  resolveFetch(response);

  assert.equal(await pending, response);
  assert.equal(cloneCalls, 0);
  assert.equal(captures.length, 0);
});
