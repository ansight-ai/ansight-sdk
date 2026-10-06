import type { TaskDefinition, TaskInvocation } from "./ansight-task.d.ts";

export const task = {
  "schemaVersion": 1,
  "appId": "ai.ansight.motion.ios-testapp",
  "title": "Verify UIKit shake delivery",
  "description": "Inject a Simulator shake, record a known accelerometer sample, and verify both motion events in the session timeline.",
  "feature": "motion",
  "platforms": ["ios"],
  "deviceKinds": ["virtual"],
  "inputSchema": { "type": "object", "properties": {}, "additionalProperties": false },
  "timeoutSeconds": 30,
  "maximumActions": 15
} satisfies TaskDefinition;

export default async function runTask({ ansight, expect }: TaskInvocation) {
  await ansight.ui.tap({ text: "Reset counters" });
  const baseline = await ansight.ui.assert({ text: "Shake waiting", exists: true });
  expect(baseline.passed, { id: "ios-shake-baseline" }).toBe(true);

  const delivery = await ansight.device.shake();
  expect(delivery.platform, { id: "ios-shake-target" }).toBe("ios");
  expect(delivery.gestureCount, { id: "ios-gesture-posted" }).toBe(1);
  const observed = await ansight.ui.waitFor({ text: "Shake detected", timeoutMs: 4_000 });
  expect(observed.satisfied, { id: "ios-app-detected-shake" }).toBe(true);

  await ansight.ui.tap({ text: "Record sample manually" });
  const sample = await ansight.ui.assert({ text: "Samples: 1", exists: true });
  expect(sample.passed, { id: "ios-app-recorded-sample" }).toBe(true);

  let timeline = await ansight.session.getTimeline({ limit: 500 });
  for (let attempt = 0; attempt < 4 && !hasCapturedMotion(timeline.events); attempt++) {
    await new Promise(resolve => setTimeout(resolve, 250));
    timeline = await ansight.session.getTimeline({ limit: 500 });
  }
  expect(hasCapturedMotion(timeline.events), { id: "ios-motion-events-in-timeline" }).toBe(true);
  expect(timeline.events.some(event => String(event.category) === "motion"), {
    id: "ios-motion-timeline-category"
  }).toBe(true);
  return { shakeDetected: true, sampleRecorded: true };
}

function hasCapturedMotion(events: Array<{ details: unknown }>): boolean {
  const details = events.map(event => JSON.stringify(event.details));
  return details.some(value => value.includes("motion.shake"))
    && details.some(value => value.includes("motion.accelerometer"));
}
