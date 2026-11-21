"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  { createAnsight } = require("..");
const sdk = () =>
  createAnsight({ appId: "test.lifecycle", enabled: true, electron: {} });
test("screen views preserve native event type, lifecycle channel, timestamp and isolated current screen", () => {
  const a = sdk(),
    details = { route: "/documents" };
  a.screenViewed("  Documents  ", details);
  details.route = "changed";
  const event = a.recordedEvents()[0];
  assert.equal(event.eventType, "ScreenViewed");
  assert.equal(event.channel, 4);
  assert.equal(event.label, "Documents");
  assert.deepEqual(JSON.parse(event.details), { route: "/documents" });
  const snapshot = a.snapshot();
  assert.equal(snapshot.currentScreen.details.route, "/documents");
  assert.equal(snapshot.currentScreen.capturedAtUtc, event.capturedAtUtc);
  snapshot.currentScreen.details.route = "tampered";
  assert.equal(a.snapshot().currentScreen.details.route, "/documents");
  assert.throws(() => a.screenViewed(" "), /Screen name/);
  assert.throws(() => a.screenViewed("Screen", { count: 2 }), /string/);
  assert.throws(() => a.screenViewed("Screen", {}, 254), /registered/);
});
test("explicit lifecycle is deduplicated, timestamped, retained and survives sampling until automatic tracking resumes", async () => {
  const a = sdk(),
    sent = [];
  a.send = async (m) => sent.push(m);
  a.streams.windows.set(1, {
    isDestroyed: () => false,
    isVisible: () => true,
    isMinimized: () => false,
  });
  const timestamp = "2026-11-21T00:00:00.000Z";
  a.setAppLifecycleState("background", timestamp);
  a.setAppLifecycleState("background");
  a.streams.lifecycle();
  assert.equal(a.recordedEvents().length, 1);
  assert.equal(a.recordedEvents()[0].label, "lifecycle.background");
  assert.equal(a.recordedEvents()[0].capturedAtUtc, timestamp);
  assert.equal(a.snapshot().lifecycleState, "background");
  await a.streams.connected();
  assert.equal(a.recordedEvents().length, 1);
  assert.ok(
    sent.some((m) => m.type === "CLIENT_APP_STATE" && m.state === "background"),
  );
  a.setAppLifecycleState(null);
  assert.equal(a.snapshot().lifecycleState, "foreground");
  assert.equal(a.recordedEvents().length, 2);
  assert.throws(() => a.setAppLifecycleState("invalid"), /Invalid/);
});
test("disabled instrumentation does not retain screen or lifecycle evidence", () => {
  const a = createAnsight({ appId: "disabled", enabled: false, electron: {} });
  a.screenViewed("Private screen");
  a.setAppLifecycleState("foreground");
  assert.deepEqual(a.recordedEvents(), []);
  assert.equal(a.snapshot().currentScreen, null);
});
