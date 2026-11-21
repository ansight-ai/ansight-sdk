"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { WindowVideoRecorder, installVideo } = require("../lib/video");
function setup(fail = false) {
  let throttling = true,
    destroyed = false;
  const target = {
    id: 7,
    isDestroyed: () => false,
    getContentSize: () => [640, 480],
    webContents: {
      getBackgroundThrottling: () => throttling,
      setBackgroundThrottling: (value) => {
        throttling = value;
      },
      getMediaSourceId: () => "scoped-id",
    },
  };
  class Window {
    constructor() {
      this.webContents = {
        setWindowOpenHandler() {},
        executeJavaScript: async (code) => {
          if (code.includes("beginRecording"))
            return { startedAtUtc: new Date().toISOString() };
          if (code.includes("finishRecording")) return 4;
          if (code.startsWith("btoa"))
            return Buffer.from("webm").toString("base64");
          return { state: "recording" };
        },
      };
    }
    async loadFile() {
      if (fail) throw new Error("load failed");
    }
    isDestroyed() {
      return destroyed;
    }
    destroy() {
      destroyed = true;
    }
  }
  const sdk = { electron: { BrowserWindow: Window }, window: () => target };
  return {
    video: new WindowVideoRecorder(sdk),
    sdk,
    restored: () => throttling,
    destroyed: () => destroyed,
  };
}
test("native video startup failure restores app rendering and closes recorder", async () => {
  const s = setup(true);
  await assert.rejects(s.video.start(), /load failed/);
  assert.equal(s.restored(), true);
  assert.equal(s.destroyed(), true);
  assert.deepEqual(await s.video.status(), { state: "idle" });
});
test("native video rejects duplicate recording and unknown cancellation without disturbing capture", async () => {
  const s = setup();
  const r = await s.video.start();
  await assert.rejects(s.video.start(), /already active/);
  assert.throws(() => s.video.cancel({ recordingId: "other" }), /Unknown/);
  assert.equal((await s.video.status()).state, "recording");
  s.video.cancel({ recordingId: r.recordingId });
  assert.equal(s.restored(), true);
});
test("native video stop queues exact binary bytes and restores settings", async () => {
  const s = setup();
  const r = await s.video.start();
  const context = { requestId: "test", transfers: [] };
  const result = await s.video.stop({ recordingId: r.recordingId }, context);
  assert.equal(context.transfers[0].bytes.toString(), "webm");
  assert.equal(result.sizeBytes, 4);
  assert.equal(result.transferId, context.transfers[0].transferId);
  assert.equal(result.mimeType, "video/webm");
  assert.equal(s.restored(), true);
  assert.deepEqual(await s.video.status(), { state: "idle" });
});
test("video mutations use the standard write guard", () => {
  const { sdk } = setup();
  const policies = {};
  sdk.registerTool = (d) => {
    policies[d.id] = d.policy;
  };
  installVideo(sdk);
  assert.deepEqual(policies, {
    "electron.video.start": "write",
    "electron.video.status": "read",
    "electron.video.stop": "write",
    "electron.video.cancel": "write",
  });
});
