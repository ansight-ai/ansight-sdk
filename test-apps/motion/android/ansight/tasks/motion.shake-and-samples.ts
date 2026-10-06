import type { TaskDefinition, TaskInvocation } from "./ansight-task.d.ts";

export const task = {
  "schemaVersion": 1,
  "appId": "ai.ansight.motion.testapp",
  "title": "Verify motion capture from Android emulator",
  "description": "Prove that app-owned sensor listening gates capture, then inject a shake and a known accelerometer sample and verify app and timeline evidence.",
  "feature": "motion",
  "platforms": ["android"],
  "deviceKinds": ["virtual"],
  "inputSchema": { "type": "object", "properties": {}, "additionalProperties": false },
  "timeoutSeconds": 60,
  "maximumActions": 30
} satisfies TaskDefinition;

export default async function runTask({ ansight, expect }: TaskInvocation) {
  const disabled = await ansight.ui.waitFor({ text: "Listener: disabled", timeoutMs: 4_000 });
  expect(disabled.satisfied, { id: "sensor-listener-disabled" }).toBe(true);

  await ansight.device.playAccelerometer({
    samples: [{ x: 32, y: 0, z: 0, holdMs: 300 }]
  });
  const noSamples = await ansight.ui.assert({ text: "Samples: 0", exists: true });
  expect(noSamples.passed, { id: "no-app-samples-while-disabled" }).toBe(true);
  const before = await ansight.session.getTimeline({ limit: 500 });
  expect(before.events.some(event => JSON.stringify(event.details).includes("motion.accelerometer")), {
    id: "no-sdk-accelerometer-while-disabled"
  }).toBe(false);

  await ansight.ui.tap({ text: "Enable accelerometer" });
  const enabled = await ansight.ui.waitFor({ text: "Listener: enabled", timeoutMs: 4_000 });
  expect(enabled.satisfied, { id: "sensor-listener-enabled" }).toBe(true);
  await ansight.ui.tap({ text: "Reset counters" });

  const shake = await ansight.device.shake({ intensity: 22, repetitions: 3, intervalMs: 90 });
  expect(shake.sampleCount, { id: "shake-pulses-delivered" }).toBe(6);
  const shakeSeen = await ansight.ui.waitFor({ text: "Shake detected", timeoutMs: 4_000 });
  expect(shakeSeen.satisfied, { id: "app-detected-shake" }).toBe(true);

  await ansight.ui.tap({ text: "Reset counters" });
  const sequence = await ansight.device.playAccelerometer({
    samples: [
      { x: 0, y: 9.81, z: 0, holdMs: 100 },
      { x: 32, y: 0, z: 0, holdMs: 250 },
      { x: 0, y: 9.81, z: 0, holdMs: 100 }
    ]
  });
  expect(sequence.sampleCount, { id: "custom-sequence-delivered" }).toBe(3);
  const sampleSeen = await ansight.ui.waitFor({ text: "Custom sample observed", timeoutMs: 4_000 });
  expect(sampleSeen.satisfied, { id: "app-observed-custom-sample" }).toBe(true);

  let timeline = await ansight.session.getTimeline({ limit: 500 });
  for (let attempt = 0; attempt < 4 && !hasCapturedSamples(timeline.events); attempt++) {
    await new Promise(resolve => setTimeout(resolve, 250));
    timeline = await ansight.session.getTimeline({ limit: 500 });
  }
  expect(hasCapturedSamples(timeline.events), { id: "motion-events-in-timeline" }).toBe(true);
  expect(timeline.events.some(event => String(event.category) === "motion"), {
    id: "motion-timeline-category"
  }).toBe(true);

  return { shakeDetected: true, customSampleObserved: true };
}

function hasCapturedSamples(events: Array<{ details: unknown }>): boolean {
  const details = events.map(event => JSON.stringify(event.details));
  return details.some(value => value.includes("motion.shake"))
    && details.some(value => value.includes("motion.accelerometer"));
}
