import { describe, expect, it, vi } from "vitest";

const native = vi.hoisted(() => ({
  initialize: vi.fn(async () => ({})),
  initializeAndActivate: vi.fn(async () => ({})),
  hostConnectionStatus: vi.fn(async () => ({})),
  hostConnectionCapabilities: vi.fn(async () => ({})),
  addListener: vi.fn(async () => ({ remove: vi.fn() })),
}));
vi.mock("@capacitor/core", () => ({
  Capacitor: { getPlatform: () => "ios", isNativePlatform: () => true },
  registerPlugin: () => native,
}));

import { initialize, initializeAndActivate } from "../src/index";

describe("crash handoff native settings", () => {
  for (const [method, invoke] of [
    ["initialize", initialize],
    ["initializeAndActivate", initializeAndActivate],
  ] as const) {
    it(`${method} forwards explicit host handoff settings`, async () => {
      for (const crashCapture of [
        { hostHandoffEnabled: false },
        { hostHandoffEnabled: true },
      ]) {
        await invoke({ crashCapture, lifecycle: false, networkCapture: false });
        expect(native[method]).toHaveBeenLastCalledWith(
          expect.objectContaining({
            crashCapture,
          }),
        );
      }
      await invoke({
        crashCapture: false,
        lifecycle: false,
        networkCapture: false,
      });
      expect(native[method]).toHaveBeenLastCalledWith(
        expect.objectContaining({ crashCapture: false }),
      );
    });
  }
});
