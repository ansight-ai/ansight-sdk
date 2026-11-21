"use strict";
const { app, BrowserWindow } = require("electron"),
  fs = require("node:fs"),
  path = require("node:path"),
  assert = require("node:assert/strict");
const { createAnsight } = require("..");
const root = process.env.ANSIGHT_RUNTIME_FIXTURE_DIR;
if (!root) throw new Error("fixture directory required");
fs.mkdirSync(root, { recursive: true });
app.setPath("userData", path.join(root, "user-data"));
const timeout = setTimeout(() => app.exit(2), 30000);
app
  .whenReady()
  .then(async () => {
    const w = new BrowserWindow({
      width: 400,
      height: 300,
      webPreferences: {
        sandbox: true,
        contextIsolation: true,
        nodeIntegration: false,
      },
    });
    await w.loadURL(
      "data:text/html,<h1>Ansight custom telemetry verification</h1>",
    );
    const sdk = createAnsight({
      appId: "com.ansight.electron.network.fixture",
      appName: "Electron manual network verification",
      enabled: true,
      discoveryPort: 47123,
      sessionJpegCapture: false,
      additionalChannels: [
        {
          id: 42,
          name: "Verified documents",
          unit: "count",
          kind: "documents.verified",
        },
      ],
    });
    sdk.attachWindow(w);
    await sdk.start();
    while (!sdk.connected) await new Promise((r) => setTimeout(r, 100));
    const http = require("node:http");
    const server = http.createServer((req, res) => {
      res.writeHead(201, { "Content-Type": "application/json" });
      res.end(
        JSON.stringify({ documentCount: 17, token: "fixture-response-secret" }),
      );
    });
    await new Promise((r) => server.listen(0, "127.0.0.1", r));
    const started = new Date().toISOString(),
      before = performance.now();
    const url = `http://127.0.0.1:${server.address().port}/documents?page=2&token=fixture-query-secret`;
    const response = await fetch(url, {
      headers: { Authorization: "Bearer fixture-header-secret" },
    });
    const body = await response.text();
    sdk.recordNetworkRequest({
      id: "manual-network-fixture",
      source: "electron.app.fixture",
      url,
      method: "GET",
      startedAtUtc: started,
      completedAtUtc: new Date().toISOString(),
      durationMilliseconds: performance.now() - before,
      statusCode: response.status,
      requestHeaders: [
        { name: "Authorization", value: "Bearer fixture-header-secret" },
      ],
      responseHeaders: [
        { name: "Content-Type", value: response.headers.get("content-type") },
      ],
      responseBody: {
        encoding: "utf8",
        contentType: "application/json",
        data: body,
        totalBytes: Buffer.byteLength(body),
      },
      responseBodySizeBytes: Buffer.byteLength(body),
    });
    server.closeAllConnections();
    await new Promise((r) => server.close(r));
    sdk.metric(17, 42);
    sdk.event("Runtime parity probe", { documents: 17 });
    assert.equal(sdk.recordedMetrics().find((m) => m.channel === 42).value, 17);
    assert.ok(
      sdk.recordedEvents().some((e) => e.label === "Runtime parity probe"),
    );
    fs.writeFileSync(
      path.join(root, "ready.json"),
      JSON.stringify({ passed: true, snapshot: sdk.snapshot() }, null, 2),
    );
    await new Promise((r) => setTimeout(r, 3000));
    await sdk.stop();
    clearTimeout(timeout);
    app.quit();
  })
  .catch((e) => {
    console.error(e);
    app.exit(1);
  });
