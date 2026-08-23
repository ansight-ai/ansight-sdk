import { describe, expect, it } from "vitest";

import {
  installBrowserNetworkCapture,
  sanitizeNetworkRequest,
} from "../src/network";

describe("network capture", () => {
  it("reapplies mandatory redaction after the app sanitizer", () => {
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
        requestSanitizer: (value) => ({
          ...value,
          requestHeaders: [{ name: "Authorization", value: "Bearer restored" }],
        }),
      },
    );

    expect(request?.method).toBe("GET");
    expect(request?.url).toContain("token=%3Credacted%3E");
    expect(request?.url).not.toContain("password");
    expect(request?.requestHeaders[0]?.value).toBe("<redacted>");
  });

  it("redacts cloud signed URLs and captured text bodies", () => {
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
      });
      expect(request?.url).not.toMatch(
        /azure-secret|credential-secret|aws-secret|google-secret|gcs-secret/,
      );
      expect(request?.url).toContain("safe=yes");
      expect(request?.requestBody?.data).not.toContain("body-secret");
      expect(request?.requestBody?.data).toContain("visible");
    }
  });

  it("captures fetch metadata without consuming the response body", async () => {
    const captures: unknown[] = [];
    const response = new Response("response body", {
      status: 201,
      statusText: "Created",
      headers: {
        "content-length": "13",
        "set-cookie": "session=secret",
      },
    });
    const runtime = {
      fetch: async () => response,
      performance: { now: () => 10 },
    } as unknown as typeof globalThis;
    const subscription = installBrowserNetworkCapture(
      (request) => captures.push(request),
      {},
      "capacitor",
      runtime,
    );

    const actual = await runtime.fetch(
      "https://example.test/items?token=secret",
      {
        method: "POST",
        headers: { Authorization: "Bearer secret" },
        body: "request body",
      },
    );
    subscription.remove();

    expect(actual).toBe(response);
    expect(captures).toHaveLength(1);
    expect(response.bodyUsed).toBe(false);
    expect(captures[0]).toMatchObject({
      source: "capacitor.fetch",
      method: "POST",
      statusCode: 201,
      responseBodySizeBytes: 13,
      requestBody: { encoding: "utf8", data: "request body" },
      responseBody: { encoding: "utf8", data: "response body" },
    });
  });

  it("excludes request and response bodies independently", () => {
    const request = sanitizeNetworkRequest(
      {
        method: "POST",
        url: "https://example.test",
        requestBody: {
          encoding: "utf8",
          data: "request",
          capturedBytes: 7,
          truncated: false,
        },
        responseBody: {
          encoding: "utf8",
          data: "response",
          capturedBytes: 8,
          truncated: false,
        },
      },
      { captureResponseBody: false },
    );
    expect(request?.requestBody?.data).toBe("request");
    expect(request?.responseBody).toBeUndefined();
  });

  it("captures synchronous fetch failures", () => {
    const captures: Array<{ errorType?: string; errorMessage?: string }> = [];
    const failure = new TypeError("token=secret");
    const runtime = {
      fetch: () => {
        throw failure;
      },
      performance: { now: () => 10 },
    } as unknown as typeof globalThis;
    const subscription = installBrowserNetworkCapture(
      (request) => captures.push(request),
      {},
      "capacitor",
      runtime,
    );

    expect(() => runtime.fetch("https://example.test?token=secret")).toThrow(
      failure,
    );
    subscription.remove();

    expect(captures).toHaveLength(1);
    expect(captures[0]).toMatchObject({
      errorType: "TypeError",
      errorMessage: "token=<redacted>",
    });
  });

  it("ignores an in-flight response after capture is removed", async () => {
    const captures: unknown[] = [];
    let resolveFetch: ((value: Response) => void) | undefined;
    let cloneCalls = 0;
    const response = {
      status: 200,
      statusText: "OK",
      url: "https://example.test/items",
      headers: new Headers({
        "content-type": "text/plain",
        "content-length": "7",
      }),
      clone: () => {
        cloneCalls += 1;
        throw new Error("body capture should not start");
      },
    } as unknown as Response;
    const runtime = {
      fetch: () =>
        new Promise<Response>((resolve) => {
          resolveFetch = resolve;
        }),
      performance: { now: () => 10 },
    } as unknown as typeof globalThis;
    const subscription = installBrowserNetworkCapture(
      (request) => captures.push(request),
      {},
      "capacitor",
      runtime,
    );

    const pending = runtime.fetch("https://example.test/items");
    subscription.remove();
    resolveFetch?.(response);

    await expect(pending).resolves.toBe(response);
    expect(cloneCalls).toBe(0);
    expect(captures).toHaveLength(0);
  });
});
