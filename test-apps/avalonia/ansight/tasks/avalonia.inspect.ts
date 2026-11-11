import type { TaskDefinition, TaskInvocation } from "./ansight-task.d.ts";
import { captureFrameworkTree } from "../support/captureFrameworkTree.ts";

export const task = {
  "schemaVersion": 1,
  "title": "Inspect Avalonia trees and retained render metrics",
  "description": "Verify both framework trees, attached roots, numeric renderer diagnostics, and retained telemetry after navigating the sample.",
  "inputSchema": {
    "type": "object",
    "properties": {
      "requireInputMetrics": {
        "type": "boolean",
        "default": true,
        "description": "Require input-processing samples after real UI interaction. False limits the check to launch/render diagnostics."
      }
    },
    "additionalProperties": false
  },
  "maximumActions": 100,
  "timeoutSeconds": 300
} satisfies TaskDefinition;

type Diagnostics = { metrics?: { enabled: boolean; instruments: { instrument: string; available: boolean; receivedSamples: number }[] } };

export default async function runTask(invocation: TaskInvocation) {
  const { app, ansight, expect, input } = invocation;
  const requireInputMetrics = input.requireInputMetrics !== false;
  const roots = await app.callTool<{ roots: { nodeId: string }[] }>("avalonia.get_roots", {});
  if (roots.responseType === "tool.error") throw new Error(roots.payload.message);
  expect(roots.payload.result.roots.length > 0, { id: "attached-root" }).toBe(true);

  const logical = await captureFrameworkTree(invocation, "logical", 22);
  const visual = await captureFrameworkTree(invocation, "visual", 22);
  expect(Boolean(visual.rootId), { id: "visual-root-identity" }).toBe(true);
  expect(visual.rootId, { id: "shared-root-identity" }).toBe(logical.rootId);

  const diagnostics = await app.callTool<Diagnostics>("avalonia.get_rendering_diagnostics", {});
  if (diagnostics.responseType === "tool.error") throw new Error(diagnostics.payload.message);
  expect(diagnostics.payload.result.metrics?.enabled, { id: "render-metrics-enabled" }).toBe(true);
  const instruments = diagnostics.payload.result.metrics?.instruments ?? [];
  expect(instruments.filter(instrument => instrument.available).length, { id: "all-render-instruments" }).toBe(9);

  const channels = [40, 43, 46, 49, 52, 59, 61];
  if (requireInputMetrics) channels.push(55);
  for (const channelId of channels) {
    const telemetry = await ansight.telemetry.get({ channelIds: [channelId], limit: 10 });
    expect(telemetry.matchedSampleCount > 0, {
      id: `retained-metric-${channelId}`,
      message: `Channel ${channelId} must contain retained numeric samples; an available tool alone does not pass.`
    }).toBe(true);
  }

  return { logicalNodes: logical.nodes.length, visualNodes: visual.nodes.length,
    logicalSnapshots: logical.snapshotIds, visualSnapshots: visual.snapshotIds,
    inputMetricsValidated: requireInputMetrics };
}
