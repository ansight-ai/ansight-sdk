"use strict";

const assert = require("node:assert/strict");
const Module = require("node:module");
const test = require("node:test");

const emitted = [];
const originalLoad = Module._load;
Module._load = function load(request, parent, isMain) {
  if (request === "@ansight/react-native") {
    return {
      sendSessionEvent: async (type, payload) => {
        emitted.push({ type, payload });
        return { success: true, message: "sent" };
      },
    };
  }
  return originalLoad.call(this, request, parent, isMain);
};

const { AnsightLocationRecorder } = require("../index.js");
Module._load = originalLoad;

test("capture is disabled by default", async () => {
  emitted.length = 0;
  const recorder = new AnsightLocationRecorder();

  const result = await recorder.record({ latitude: -33.8688, longitude: 151.2093 });

  assert.equal(result.success, false);
  assert.equal(emitted.length, 0);
});

test("enabled recorder emits a privacy-reduced sample through the existing runtime", async () => {
  emitted.length = 0;
  const recorder = new AnsightLocationRecorder({
    enabled: true,
    decimalPlaces: 3,
    minimumIntervalMilliseconds: 0,
    minimumDistanceMeters: 0,
  });

  const result = await recorder.record({
    sampleId: "sample-1",
    latitude: -33.868812,
    longitude: 151.209319,
    timestamp: Date.parse("2026-08-17T01:00:00Z"),
    correlationId: "command-1",
    runId: "run-1",
  });

  assert.equal(result.success, true);
  assert.equal(emitted.length, 1);
  assert.equal(emitted[0].type, "CLIENT_LOCATION");
  assert.equal(emitted[0].payload.source, "app_observed");
  assert.equal(emitted[0].payload.latitude, -33.869);
  assert.equal(emitted[0].payload.longitude, 151.209);
  assert.equal(emitted[0].payload.correlationId, "command-1");
  assert.equal(emitted[0].payload.runId, "run-1");
});
