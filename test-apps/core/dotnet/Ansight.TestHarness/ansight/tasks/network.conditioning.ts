import type { TaskDefinition, TaskInvocation } from "./ansight-task.d.ts";

export const task = {
  "schemaVersion": 1,
  "appId": "ai.ansight.dotnet.maui-harness",
  "title": "Verify network conditioning and recovery",
  "description": "Prove a baseline transfer, a rate-limited transfer, TCP failure and recovery against the local network fixture.",
  "feature": "connectivity",
  "platforms": [
    "ios",
    "android"
  ],
  "deviceKinds": [
    "virtual"
  ],
  "inputSchema": {
    "type": "object",
    "properties": {
      "endpoint": {
        "type": "string",
        "description": "Fixture URL using the Mac's LAN address; loopback bypasses macOS local capture. Return at least 256 KiB."
      }
    },
    "additionalProperties": false,
    "required": ["endpoint"]
  },
  "timeoutSeconds": 90,
  "maximumActions": 30
} satisfies TaskDefinition;

export default async function run({ ansight, expect, input }: TaskInvocation<{ endpoint: string }>) {
  const endpoint = new URL(input.endpoint);
  if (!["http:", "https:"].includes(endpoint.protocol)
    || ["localhost", "[::1]", "10.0.2.2"].includes(endpoint.hostname)
    || endpoint.hostname.startsWith("127.")) {
    throw new Error("Use the fixture URL with the Mac's LAN address; loopback traffic bypasses macOS local capture.");
  }
  const capability = await ansight.device.network.capabilities();
  expect(capability.available, { id: "network-conditioner-available", message: capability.reason ?? undefined }).toBe(true);
  await ansight.ui.typeText({ automationId: "network.endpoint", value: input.endpoint, replaceExisting: true });
  await ansight.keyboard.dismiss();
  await ansight.ui.tap({ automationId: "network.probe" });
  const baseline = await ansight.ui.waitFor({ automationId: "network.status", text: "Succeeded", timeoutMs: 10000 });
  expect(baseline.satisfied, { id: "network-baseline-succeeded" }).toBe(true);

  const lease = await ansight.device.network.set({
    profile: { downloadKbps: 256, latencyMs: 100, jitterMs: 20, seed: 42 },
    destinationPorts: [Number(endpoint.port || (endpoint.protocol === "https:" ? "443" : "80"))],
    durationMs: 60000
  });
  try {
    await ansight.ui.tap({ automationId: "network.probe" });
    const limited = await ansight.ui.waitFor({ automationId: "network.status", text: "Succeeded", timeoutMs: 18000 });
    expect(limited.satisfied, { id: "network-limited-transfer-succeeded" }).toBe(true);
    // A successful request alone could mean that traffic escaped the selected process.
    let observed = await ansight.device.network.get({ leaseId: lease.leaseId });
    for (let attempt = 0; attempt < 4 && observed.counters.downloadBytes < 262144; attempt++) {
      await new Promise(resolve => setTimeout(resolve, 250));
      observed = await ansight.device.network.get({ leaseId: lease.leaseId });
    }
    expect(observed.counters.downloadBytes >= 262144, { id: "network-proxy-observed-transfer" }).toBe(true);

    await ansight.device.network.set({ leaseId: lease.leaseId, profile: { blocked: true } });
    await ansight.ui.tap({ automationId: "network.probe" });
    const failure = await ansight.ui.waitFor({ automationId: "network.status", text: "Failed", timeoutMs: 10000 });
    expect(failure.satisfied, { id: "network-blocked-transfer-failed" }).toBe(true);
  } finally {
    await ansight.device.network.reset({ leaseId: lease.leaseId });
  }
  await ansight.ui.tap({ automationId: "network.probe" });
  const recovery = await ansight.ui.waitFor({ automationId: "network.status", text: "Succeeded", timeoutMs: 10000 });
  expect(recovery.satisfied, { id: "network-recovery-succeeded" }).toBe(true);
  return { recovered: true, leaseId: lease.leaseId, scope: lease.scope };
}
