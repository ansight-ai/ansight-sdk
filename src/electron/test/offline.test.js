"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  fs = require("node:fs/promises"),
  os = require("node:os"),
  path = require("node:path");
const { OfflineCapture } = require("../lib/offline"),
  { archive } = require("../lib/archive");
async function setup(t, options = {}) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "ansight-offline-"));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const sdk = {
    options: {
      appId: "test.offline",
      offlineCapture: { enabled: true, ...options },
    },
    streams: { channels: require("../lib/streams").CHANNELS },
    running: true,
    connected: false,
    processSessionId: "process-a",
    customProperties: { test: { enabled: true } },
    profile: () => ({ appId: "test.offline" }),
    registerTool: () => {},
    diagnostic: () => {},
    send: async () => {},
  };
  const store = new OfflineCapture(sdk, root);
  await store.initialize();
  return { root, sdk, store };
}
test("offline records recover after interruption, retain identity, and export deterministically", async (t) => {
  const { root, store, sdk } = await setup(t);
  store.append({ type: "CLIENT_EVENTS", events: [{ label: "retained" }] });
  await store.queue;
  const original = (await store.sessions())[0];
  const recovered = new OfflineCapture(
    { ...sdk, processSessionId: "process-b" },
    root,
  );
  await recovered.initialize();
  const s = (await recovered.sessions())[0];
  assert.equal(s.sessionId, original.sessionId);
  assert.equal(s.processSessionId, "process-a");
  assert.equal(s.terminationKind, "interrupted");
  const a = await archive(
    store.directory(s.sessionId),
    path.join(root, "a.zip"),
  );
  const b = await archive(
    store.directory(s.sessionId),
    path.join(root, "b.zip"),
  );
  assert.deepEqual(a, b);
  const bytes = await fs.readFile(path.join(root, "a.zip"));
  assert.equal(bytes.readUInt32LE(), 0x04034b50);
  assert.ok(bytes.includes(Buffer.from("records.jsonl")));
  assert.ok(bytes.includes(Buffer.from("retained")));
});
test("offline receipt mismatch retains source and successful retry purges", async (t) => {
  const { store, sdk } = await setup(t);
  store.append({ type: "CLIENT_EVENTS", events: [{ label: "offline" }] });
  await store.queue;
  sdk.connected = true;
  await store.work(() => store.seal());
  sdk.control = async () => ({
    durable: true,
    sessionId: "wrong",
    sha256: "wrong",
  });
  await assert.rejects(store.transfer(), /receipt/);
  assert.equal((await store.sessions()).length, 1);
  sdk.control = async (action, p) => ({ ...p, durable: true });
  await store.transfer();
  assert.equal((await store.sessions()).length, 0);
});
test("offline capacity, queue pressure and entitlement gate are bounded", async (t) => {
  const { store } = await setup(t, {
    maximumSessions: 2,
    maximumQueuedBytes: 2048,
  });
  store.append({ data: "x".repeat(3000) });
  assert.equal(store.dropped, 1);
  for (let i = 0; i < 4; i++) {
    store.append({ ordinal: i });
    await store.queue;
    await store.work(() => store.seal());
  }
  assert.equal((await store.sessions()).length, 2);
  await store.setEnabled(false);
  assert.equal(store.append({ denied: true }), false);
  assert.equal(store.isRecording, false);
});
test("offline rotation enforces byte limits and preserves changed properties", async (t) => {
  const { store, sdk } = await setup(t, {
    maximumSessionBytes: 3000,
    maximumRetainedBytes: 6000,
  });
  for (let i = 0; i < 12; i++) {
    store.append({ data: "x".repeat(600), sequence: i });
    await store.queue;
  }
  const sessions = await store.sessions();
  assert.ok(sessions.every((s) => s.bytes <= 3000));
  assert.ok(sessions.reduce((n, s) => n + s.bytes, 0) <= 6000);
  sdk.customProperties = { fixture: { updated: true } };
  await store.updateProperties();
  assert.deepEqual(
    JSON.parse(
      await fs.readFile(
        path.join(
          store.directory(store.current.sessionId),
          "metadata/custom-properties.json",
        ),
        "utf8",
      ),
    ),
    sdk.customProperties,
  );
});
