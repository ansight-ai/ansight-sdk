import { beforeEach, describe, expect, it, vi } from "vitest";
const native = vi.hoisted(() => ({
  initialize: vi.fn<(options: unknown) => Promise<{ registeredTools: number }>>(
    async () => ({ registeredTools: 0 }),
  ),
  initializeAndActivate: vi.fn<
    (options: unknown) => Promise<{ registeredTools: number }>
  >(async () => ({
    registeredTools: 0,
  })),
  snapshot: vi.fn(async () => ({ registeredTools: 1 })),
  hostConnectionStatus: vi.fn(async () => ({})),
  hostConnectionCapabilities: vi.fn(async () => ({})),
}));
vi.mock("@capacitor/core", () => ({
  Capacitor: { getPlatform: () => "ios", isNativePlatform: () => true },
  registerPlugin: () => native,
}));
import {
  createOptionsBuilder,
  initialize,
  initializeAndActivate,
} from "../src/index";

describe("optional initialization plugins", () => {
  beforeEach(() => vi.clearAllMocks());
  it("builds passive isolated snapshots without losing callbacks", () => {
    const plugin = { id: "optional", initialize: vi.fn(async () => {}) };
    const builder = createOptionsBuilder()
      .withPlugin(plugin)
      .withAnsightDefaults()
      .withPlugin(plugin);
    const first = builder.build();
    const second = createOptionsBuilder(first)
      .withReadWriteToolAccess()
      .build();
    expect(second.plugins).toEqual([plugin]);
    expect(second.plugins).not.toBe(first.plugins);
    expect(second.plugins?.[0].initialize).toBe(plugin.initialize);
    expect(plugin.initialize).not.toHaveBeenCalled();
    expect(second.toolGuard).toBe("readWrite");
  });
  for (const [name, initializeSdk] of Object.entries({
    initialize,
    initializeAndActivate,
  })) {
    it(`${name} awaits plugins after native setup and refreshes the snapshot`, async () => {
      let release!: () => void;
      const gate = new Promise<void>((resolve) => {
        release = resolve;
      });
      let reached!: () => void;
      const entered = new Promise<void>((resolve) => {
        reached = resolve;
      });
      const plugin = {
        id: "optional",
        async initialize() {
          expect(native[name as "initialize"]).toHaveBeenCalledOnce();
          reached();
          await gate;
        },
      };
      const options = createOptionsBuilder({
        lifecycle: false,
        networkCapture: false,
      })
        .withReadWriteToolAccess()
        .withPlugin(plugin)
        .build();
      let complete = false;
      const pending = initializeSdk(options).then((value) => {
        complete = true;
        return value;
      });
      await entered;
      expect(complete).toBe(false);
      expect(native[name as "initialize"].mock.calls[0][0]).not.toHaveProperty(
        "plugins",
      );
      expect(native[name as "initialize"].mock.calls[0][0]).toHaveProperty(
        "toolGuard",
        "readWrite",
      );
      release();
      expect((await pending).registeredTools).toBe(1);
    });
  }
  it("does not run plugins if native initialization fails", async () => {
    native.initialize.mockRejectedValueOnce(new Error("native failed"));
    const plugin = { id: "optional", initialize: vi.fn(async () => {}) };
    await expect(
      initialize(createOptionsBuilder().withPlugin(plugin).build()),
    ).rejects.toThrow("native failed");
    expect(plugin.initialize).not.toHaveBeenCalled();
  });
  it("propagates plugin errors and skips remaining setup", async () => {
    const second = { id: "second", initialize: vi.fn(async () => {}) };
    const options = createOptionsBuilder()
      .withPlugin({
        id: "first",
        async initialize() {
          throw new Error("plugin failed");
        },
      })
      .withPlugin(second)
      .build();
    await expect(initializeAndActivate(options)).rejects.toThrow(
      "plugin failed",
    );
    expect(second.initialize).not.toHaveBeenCalled();
  });
});
