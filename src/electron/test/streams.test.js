"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { Streams } = require("../lib/streams");
test("same-millisecond pointer transitions retain distinct IDs and native detail fields", async () => {
  const capturedAtUtc = "2026-11-21T07:00:00.000Z";
  const events = [0, 2].map((action) => ({
    kind: "pointer",
    action,
    pointerId: 2,
    x: 10,
    y: 20,
    width: 100,
    height: 100,
    scale: 2,
    pointerType: "mouse",
    capturedAtUtc,
  }));
  const sdk = {
    options: { enabled: true },
    connected: false,
    runDom: async () => ({
      events,
      frames: 0,
      elapsedMs: 400,
      dropped: 0,
      visible: true,
    }),
  };
  const streams = new Streams(sdk);
  streams.interval = 400;
  streams.expected = 0;
  streams.windows.set(1, {
    id: 1,
    isDestroyed: () => false,
    isVisible: () => true,
    isMinimized: () => false,
    webContents: { isLoadingMainFrame: () => false },
  });
  await streams.sample();
  const touches = streams.queue.filter((r) => r.type === "CLIENT_TOUCH_INPUT");
  assert.equal(touches.length, 2);
  assert.notEqual(touches[0].ids[0], touches[1].ids[0]);
  assert.equal(touches[0].t0, capturedAtUtc);
  assert.equal(touches[1].t0, capturedAtUtc);
  assert.deepEqual(
    touches.map((t) => t.rows[0][1]),
    [0, 2],
  );
  assert.equal(touches[1].rows[0][7].tool, "mouse");
});
