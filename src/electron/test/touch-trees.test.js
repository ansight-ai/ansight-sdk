"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { TouchTreeCoordinator } = require("../lib/touch-trees");
const wait = (ms) => new Promise((r) => setTimeout(r, ms));
const touch = (action, pointerId = 1) => ({
  action,
  pointerId,
  capturedAtUtc: "2026-11-21T07:00:00.000Z",
});
test("touch trees preserve gesture identity and throttle end capture", async (t) => {
  const captured = [];
  const c = new TouchTreeCoordinator((trigger) => captured.push(trigger), 750);
  t.after(() => c.close());
  c.observe(touch(0));
  await wait(20);
  c.observe(touch(1));
  c.observe(touch(2));
  assert.equal(captured.length, 1);
  assert.equal(captured[0].gesturePhase, "started");
  await wait(800);
  assert.equal(captured.length, 2);
  assert.equal(captured[1].gesturePhase, "ended");
  assert.equal(captured[1].gestureId, captured[0].gestureId);
  assert.equal(captured[1].touchCapturedAtUtc, touch(2).capturedAtUtc);
});
test("coalesced tap retains start; cancellation and close clear queued work", async () => {
  const captured = [];
  const c = new TouchTreeCoordinator((trigger) => captured.push(trigger));
  c.observe(touch(0));
  c.observe(touch(2));
  await wait(20);
  assert.equal(captured.length, 1);
  assert.equal(captured[0].gesturePhase, "started");
  c.observe(touch(0));
  c.observe(touch(3));
  assert.equal(c.pending, null);
  c.close();
  c.observe(touch(0));
  assert.equal(c.pending, null);
});
