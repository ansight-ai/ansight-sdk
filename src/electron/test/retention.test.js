"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { Retention, normalizedTiming } = require("../lib/retention");
const { createAnsight } = require("..");
test("retained evidence expires by receipt time and readback is isolated", () => {
  let now = 100000;
  const r = new Retention(60, () => now);
  r.append({
    metrics: [{ channel: 42, value: 9, capturedAtUtc: "app-clock" }],
    events: [{ label: "sample" }],
  });
  const metrics = r.read("metrics");
  metrics[0].value = 0;
  assert.equal(r.read("metrics")[0].value, 9);
  now += 60001;
  assert.deepEqual(r.read("metrics"), []);
  assert.deepEqual(r.read("events"), []);
  assert.equal(r.eventBytes, 0);
});
test("timing uses native bounds and canonical option wins over legacy alias", () => {
  assert.deepEqual(normalizedTiming({}), {
    sampleFrequencyMilliseconds: 400,
    retentionPeriodSeconds: 120,
  });
  assert.equal(
    normalizedTiming({
      sampleFrequencyMilliseconds: 900,
      sampleIntervalMilliseconds: 200,
    }).sampleFrequencyMilliseconds,
    900,
  );
  assert.deepEqual(
    normalizedTiming({
      sampleIntervalMilliseconds: 1,
      retentionPeriodSeconds: 9999,
    }),
    { sampleFrequencyMilliseconds: 200, retentionPeriodSeconds: 3600 },
  );
  assert.throws(
    () => normalizedTiming({ sampleFrequencyMilliseconds: NaN }),
    /finite/,
  );
});
test("custom metrics use registered channels and remain readable when offline owns transport", () => {
  const sdk = createAnsight({
    appId: "test.metrics",
    enabled: true,
    electron: {},
    additionalChannels: [{ id: 42, name: "Pending documents", unit: "count" }],
  });
  const sent = [];
  sdk.offline = {
    append: (m) => {
      sent.push(m);
      return true;
    },
  };
  assert.equal(sdk.metric(7, 42), true);
  sdk.event("Document indexed", { count: 7 });
  assert.equal(sent.length, 2);
  assert.equal(sdk.streams.queue.length, 0);
  assert.equal(sdk.recordedMetrics()[0].value, 7);
  assert.equal(sdk.snapshot().eventsRecorded, 1);
  assert.equal(sdk.metric(1, 43), false);
  assert.throws(() => sdk.metric(Infinity, 42), /finite/);
  assert.throws(() => sdk.metric(1, 256), /255/);
  sdk.clear();
  assert.equal(sdk.snapshot().metricsRecorded, 0);
  assert.equal(sent.length, 2);
  assert.throws(
    () =>
      createAnsight({
        appId: "test",
        enabled: false,
        electron: {},
        additionalChannels: [{ id: 100, name: "Collision" }],
      }),
    /duplicate/,
  );
});
test("disabled SDK records no custom data and retention has a hard capacity", () => {
  const sdk = createAnsight({ appId: "test", enabled: false, electron: {} });
  assert.equal(sdk.metric(3), false);
  sdk.event("ignored");
  assert.deepEqual(sdk.recordedEvents(), []);
  const r = new Retention(120);
  r.append({
    metrics: Array.from({ length: 100005 }, (_, value) => ({ value })),
  });
  assert.equal(r.read("metrics").length, 100000);
  assert.equal(r.read("metrics")[0].value, 5);
});
