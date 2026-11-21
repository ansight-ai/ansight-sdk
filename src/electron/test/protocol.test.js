"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  dgram = require("node:dgram");
const { enroll, jpegFrame, fileFrame } = require("../lib/protocol");
const { createAnsight } = require("..");
test("binary frames match native SDK wire layouts", () => {
  const jpg = jpegFrame(Buffer.from([1, 2, 3]), 100, 200, 70, 12345);
  assert.equal(jpg.subarray(0, 4).toString(), "ASJP");
  assert.equal(jpg.readBigInt64LE(8), 12345n);
  assert.equal(jpg.readInt32LE(16), 100);
  assert.equal(jpg.readInt32LE(20), 200);
  assert.equal(jpg.readInt32LE(24), 3);
  const frame = fileFrame(
    "12345678-1234-1234-1234-123456789abc",
    1,
    2,
    65536,
    Buffer.from("data"),
  );
  assert.equal(
    frame.subarray(8, 40).toString(),
    "12345678123412341234123456789abc",
  );
  assert.equal(frame.readInt32LE(40), 2);
  assert.equal(frame.readBigInt64LE(44), 65536n);
  assert.equal(frame.subarray(56).toString(), "data");
});
test("local enrollment ignores unrelated replies and pins loopback handoff", async () => {
  const server = dgram.createSocket("udp4");
  await new Promise((r) => server.bind(0, "127.0.0.1", r));
  server.on("message", (bytes, peer) => {
    const req = JSON.parse(bytes);
    assert.equal(req.enrollmentMode, "local");
    assert.equal(req.inviteId, "local:test");
    const reply = {
      type: "ENROLLMENT_RESULT",
      ver: 2,
      requestId: req.requestId,
      accepted: true,
      webSocketPort: 1234,
      webSocketPath: "/ws",
      webSocketToken: "a&b",
    };
    server.send(
      JSON.stringify({ ...reply, requestId: "wrong" }),
      peer.port,
      peer.address,
    );
    server.send(JSON.stringify(reply), peer.port, peer.address);
  });
  try {
    assert.equal(
      await enroll({
        appId: "test",
        appName: "Test",
        deviceId: "d",
        accessToken: "s",
        processSessionId: "p",
        port: server.address().port,
      }),
      "ws://127.0.0.1:1234/ws?token=a%26b",
    );
  } finally {
    server.close();
  }
});
test("disabled SDK never touches user data or connects", async () => {
  const sdk = createAnsight({
    appId: "test",
    enabled: false,
    electron: {
      app: {
        getPath() {
          throw new Error("must not access disk");
        },
      },
    },
  });
  await sdk.start();
  assert.equal(sdk.running, false);
});
test("packaged apps require explicit QA opt-in", async () => {
  const sdk = createAnsight({
    appId: "test",
    enabled: true,
    electron: { app: { isPackaged: true } },
  });
  await assert.rejects(sdk.start(), /allowPackaged/);
});
test("write policy rejects mutations and unknown tools return correlated errors", async () => {
  const sdk = createAnsight({
    appId: "test",
    enabled: true,
    electron: { app: {} },
  });
  const sent = [];
  sdk.send = async (value) => sent.push(value);
  let changed = false;
  sdk.registerTool({ id: "test.write", policy: "write" }, () => {
    changed = true;
  });
  await sdk.receive(
    Buffer.from(
      JSON.stringify({
        type: "tool.call",
        id: "call1",
        payload: { toolId: "test.write" },
      }),
    ),
  );
  assert.equal(changed, false);
  assert.equal(sent[0].type, "tool.error");
  assert.equal(sent[0].replyTo, "call1");
  await sdk.receive(
    Buffer.from(
      JSON.stringify({
        type: "tool.call",
        id: "call2",
        payload: { toolId: "missing" },
      }),
    ),
  );
  assert.equal(sent[1].type, "tool.error");
});
test("screenshot metadata precedes correctly chunked binary transfer", async () => {
  const sdk = createAnsight({
    appId: "test",
    enabled: true,
    electron: { app: {} },
  });
  const sent = [];
  sdk.send = async (v) => sent.push(v);
  sdk.registerTool({ id: "test.artifact", policy: "read" }, (_a, c) => {
    c.transfers.push({
      transferId: "a".repeat(32),
      bytes: Buffer.alloc(70000, 42),
    });
    return { transferId: "a".repeat(32) };
  });
  await sdk.receive(
    Buffer.from(
      JSON.stringify({
        type: "tool.call",
        id: "a",
        payload: { toolId: "test.artifact" },
      }),
    ),
  );
  assert.equal(sent[0].type, "tool.result");
  assert.equal(sent.length, 4);
  assert.equal(sent[1].readInt32LE(52), 65536);
  assert.equal(sent[2].readBigInt64LE(44), 65536n);
  assert.equal(sent[3][5], 2);
});
test("batch stops at failure and preserves earlier results", async () => {
  const sdk = createAnsight({
    appId: "test",
    enabled: true,
    electron: { app: {} },
  });
  const sent = [];
  sdk.send = async (v) => sent.push(v);
  let calls = 0;
  sdk.registerTool({ id: "test.read", policy: "read" }, () => ({
    calls: ++calls,
  }));
  await sdk.receive(
    Buffer.from(
      JSON.stringify({
        type: "tool.batch",
        id: "b",
        payload: {
          calls: [
            { toolId: "test.read" },
            { toolId: "missing" },
            { toolId: "test.read" },
          ],
        },
      }),
    ),
  );
  assert.equal(calls, 1);
  assert.equal(sent[0].type, "tool.batch.result");
  assert.equal(sent[0].payload.stoppedEarly, true);
  assert.equal(sent[0].payload.success, false);
});
test("post-call evidence reflects resulting state", async () => {
  const sdk = createAnsight({
    appId: "test",
    enabled: true,
    allowActions: true,
    electron: { app: {} },
  });
  let state = "before";
  const sent = [];
  sdk.send = async (v) => sent.push(v);
  sdk.tools.get("ui.get_visual_tree").handler = () => ({ state });
  sdk.registerTool({ id: "test.write", policy: "write" }, () => {
    state = "after";
    return { changed: true };
  });
  await sdk.receive(
    Buffer.from(
      JSON.stringify({
        type: "tool.call",
        id: "c",
        payload: { toolId: "test.write", after: { include: ["visualTree"] } },
      }),
    ),
  );
  assert.equal(sent[0].payload.evidence.visualTree.result.state, "after");
});
test("network observation strips credentials, queries and bodies", async () => {
  const { EventEmitter } = require("node:events");
  const { attachNetwork } = require("../lib/network");
  const api = new EventEmitter();
  let attached = false;
  api.isAttached = () => attached;
  api.attach = () => {
    attached = true;
  };
  api.detach = () => {
    attached = false;
  };
  api.sendCommand = async () => {};
  const records = [];
  const detach = attachNetwork(
    { id: 1, debugger: api, isDestroyed: () => false },
    (r) => records.push(r),
    (e) => {
      throw e;
    },
  );
  api.emit("message", {}, "Network.requestWillBeSent", {
    requestId: "1",
    wallTime: 1,
    timestamp: 2,
    request: {
      url: "https://user:pass@example.com/api?token=secret#secret",
      method: "GET",
      postData: "secret",
    },
  });
  api.emit("message", {}, "Network.responseReceived", {
    requestId: "1",
    response: { status: 200, protocol: "h2" },
  });
  api.emit("message", {}, "Network.dataReceived", {
    requestId: "1",
    dataLength: 128,
  });
  api.emit("message", {}, "Network.loadingFinished", {
    requestId: "1",
    timestamp: 2.5,
    encodedDataLength: 12,
  });
  assert.equal(records[0].url, "https://example.com/api");
  assert.equal(records[0].durationMilliseconds, 500);
  assert.equal(records[0].statusCode, 200);
  assert.equal(records[0].responseBodySizeBytes, 128);
  assert.equal(records[0].completedAtUtc, "1970-01-01T00:00:01.500Z");
  assert.ok(!JSON.stringify(records).includes("secret"));
  detach();
  assert.equal(attached, false);
  assert.equal(api.listenerCount("message"), 0);
});

test("standard guard ordering protects critical operations and disabled discovery", async () => {
  for (const [toolGuard, allowed] of [
    ["disabled", []],
    ["readOnly", ["read"]],
    ["readWrite", ["read", "write"]],
    ["fullAccess", ["read", "write", "critical"]],
  ]) {
    const sdk = createAnsight({
      appId: "test",
      enabled: true,
      toolGuard,
      electron: { app: {} },
    });
    let invoked = 0;
    for (const policy of ["read", "write", "critical"]) {
      sdk.registerTool({ id: "test." + policy, policy }, () => {
        invoked++;
        return {};
      });
      const call = sdk.execute(
        { toolId: "test." + policy, arguments: {} },
        { transfers: [] },
      );
      if (allowed.includes(policy)) await call;
      else await assert.rejects(call, /toolGuard/);
    }
    assert.equal(invoked, allowed.length);
    const sent = [];
    sdk.send = async (v) => sent.push(v);
    await sdk.receive(
      Buffer.from(
        JSON.stringify({ type: "tool.query", id: "guard-query", payload: {} }),
      ),
    );
    if (toolGuard === "disabled") assert.equal(sent[0].payload.tools.length, 0);
    else
      assert.equal(
        sent[0].payload.tools.find((t) => t.id === "test.critical").executable,
        toolGuard === "fullAccess",
      );
  }
});
test("host screenshot pause control uses the shared paused contract", async () => {
  const sdk = createAnsight({
    appId: "test",
    enabled: true,
    electron: { app: {} },
  });
  const sent = [];
  sdk.send = async (v) => sent.push(v);
  for (const paused of [true, false]) {
    await sdk.receive(
      Buffer.from(
        JSON.stringify({
          type: "session.screenshots",
          id: "pause",
          payload: { paused },
        }),
      ),
    );
    assert.equal(sdk.periodicPaused, paused);
    assert.deepEqual(sent.at(-1).payload, { paused });
  }
  assert.deepEqual(sdk.captureOptions, {
    intervalMilliseconds: 2000,
    quality: 60,
    maxWidth: 480,
    mode: "screenshotOnly",
  });
});
