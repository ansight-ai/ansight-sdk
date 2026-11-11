import type { NetworkRequestSummary, TaskDefinition, TaskInvocation } from "./ansight-task.d.ts";

export const task = {
  "schemaVersion": 1,
  "title": "Validate captured HTTP fixture requests",
  "description": "Verify retained JSON, HTTP 500, binary and POST exchanges against independent fixture results and correlation headers.",
  "maximumActions": 80,
  "timeoutSeconds": 120
} satisfies TaskDefinition;

type FixtureResponse = {
  requestId: string;
  scenario: string;
  statusCode: number;
  bodyBytes: number;
  bodySha256: string;
  bodyText?: string;
};

export default async function runTask({ app, ansight, expect }: TaskInvocation) {
  const requests: { scenario: string; requestId: string; correlationId: string }[] = [];
  for (const scenario of ["json", "error", "binary", "echo"]) {
    const startedAt = new Date().toISOString();
    const response = await app.callTool<FixtureResponse>("benchmark.request_network", { scenario });
    if (response.responseType === "tool.error") throw new Error(response.payload.message);
    const fixture = response.payload.result;
    const expectedStatus = scenario === "error" ? 500 : 200;
    expect(fixture.statusCode, { id: `${scenario}-fixture-status` }).toBe(expectedStatus);

    let captured: NetworkRequestSummary | undefined;
    for (let attempt = 0; attempt < 10 && !captured; attempt++) {
      const page = await ansight.network.get({ startUtc: startedAt, query: `:48733/${scenario}`, limit: 20 });
      captured = page.requests.find(request => request.url.endsWith(`/${scenario}`));
      if (!captured) await new Promise(resolve => setTimeout(resolve, 500));
    }
    expect(Boolean(captured), { id: `${scenario}-retained-request` }).toBe(true);
    if (!captured) throw new Error(`No retained ${scenario} request arrived.`);
    expect(captured.statusCode, { id: `${scenario}-captured-status` }).toBe(expectedStatus);
    expect(captured.method, { id: `${scenario}-method` }).toBe(scenario === "echo" ? "POST" : "GET");

    const detail = await ansight.network.getRequest({ requestId: captured.id });
    expect(detail.headersTruncated, { id: `${scenario}-complete-headers` }).toBe(false);
    for (const side of ["request", "response"] as const) {
      const headers = side === "request" ? detail.request.requestHeaders : detail.request.responseHeaders;
      const correlation = headers.find(header => header.name.toLowerCase() === "x-ansight-benchmark");
      expect(correlation?.value, { id: `${scenario}-${side}-correlation` }).toBe(fixture.requestId);
    }

    const body = await ansight.network.readBody({ requestId: captured.id, side: "response", maxBytes: 65536 });
    expect(body.body?.capturedBytes, { id: `${scenario}-captured-body-size` }).toBe(fixture.bodyBytes);
    expect(body.body?.truncated, { id: `${scenario}-capture-complete` }).toBe(false);
    if (scenario === "binary") {
      expect(body.body?.encoding, { id: "binary-encoding" }).toBe("base64");
      expect(body.body?.capturedBytes, { id: "binary-full-capture-size" }).toBe(262144);
      expect(body.body?.returnedByteCount, { id: "binary-preview-size" }).toBe(65536);
      expect(body.body?.isTruncated, { id: "binary-preview-is-bounded" }).toBe(true);
      const bytes = atob(body.body?.data ?? "");
      let matches = bytes.length === 65536;
      for (let index = 0; index < bytes.length && matches; index++) {
        matches = bytes.charCodeAt(index) === (index * 31 + 17) % 256;
      }
      expect(matches, { id: "binary-preview-content" }).toBe(true);
    } else {
      expect(body.body?.data, { id: `${scenario}-response-body` }).toBe(fixture.bodyText);
      expect(body.body?.isTruncated, { id: `${scenario}-read-complete` }).toBe(false);
    }
    if (scenario === "echo") {
      const requestBody = await ansight.network.readBody({ requestId: captured.id, side: "request" });
      expect(requestBody.body?.data, { id: "post-request-body" }).toBe('{"fixture":"avalonia","value":42}');
    }
    requests.push({ scenario, requestId: captured.id, correlationId: fixture.requestId });
  }
  return { requests, binaryValidation: "Full capture metadata and first 65536 bytes checked; full binary transfer is a separate gate." };
}
