import type { TaskDefinition, TaskInvocation } from "./ansight-task.d.ts";
import { captureFrameworkTree } from "../support/captureFrameworkTree.ts";

export const task = {
  "schemaVersion": 1,
  "title": "Validate retained Avalonia accessibility capture",
  "description": "Capture the framework automation-peer hierarchy and assert that retained controls include names, roles and accessibility state.",
  "maximumActions": 100,
  "timeoutSeconds": 300
} satisfies TaskDefinition;

export default async function runTask(invocation: TaskInvocation) {
  const { expect } = invocation;
  const tree = await captureFrameworkTree(invocation, "accessibility", 48);
  const peers = tree.nodes.filter(node => node.accessibility);
  expect(peers.length > 1, { id: "accessibility-has-controls" }).toBe(true);
  expect(peers.every(node => {
    const hasIdentityAndRole = Boolean(node.id)
      && Boolean(node.role)
      && Boolean(node.accessibility?.peerType);
    return hasIdentityAndRole;
  }), {
    id: "peer-identity-and-role"
  }).toBe(true);
  expect(peers.every(node => typeof node.enabled === "boolean" && typeof node.focusable === "boolean"), {
    id: "peer-states"
  }).toBe(true);
  expect(peers.some(node => Boolean(node.label)), { id: "accessible-names" }).toBe(true);
  return { snapshotIds: tree.snapshotIds, peerCount: peers.length, namedPeerCount: peers.filter(node => node.label).length };
}
