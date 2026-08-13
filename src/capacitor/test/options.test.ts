import { describe, expect, it } from "vitest";

import { createOptionsBuilder } from "../src/options";

describe("AnsightOptionsBuilder", () => {
  it("applies the cross-SDK developer defaults", () => {
    const options = createOptionsBuilder().withAnsightDefaults().build();

    expect(options).toMatchObject({
      useNativeAllInOneDefaults: true,
      sampleFrequencyMilliseconds: 400,
      retentionPeriodSeconds: 120,
      enableFramesPerSecond: true,
      enableBatteryLevel: false,
      enableOpenFileHandleTracking: false,
      enableJniReferenceCountTracking: false,
      toolGuard: "readOnly",
      hostAutoProbe: { enabled: true },
      sessionJpegCapture: {
        intervalMilliseconds: 2000,
        quality: 60,
        maxWidth: 480,
      },
    });
  });

  it("configures opt-in runtime diagnostic tracking", () => {
    const enabled = createOptionsBuilder()
      .withOpenFileHandleTracking()
      .withJniReferenceCountTracking()
      .build();

    expect(enabled.enableOpenFileHandleTracking).toBe(true);
    expect(enabled.enableJniReferenceCountTracking).toBe(true);

    const disabled = createOptionsBuilder(enabled)
      .withoutOpenFileHandleTracking()
      .withoutJniReferenceCountTracking()
      .build();

    expect(disabled.enableOpenFileHandleTracking).toBe(false);
    expect(disabled.enableJniReferenceCountTracking).toBe(false);
  });

  it("builds isolated option snapshots", () => {
    const builder = createOptionsBuilder()
      .withAnsightSdk()
      .withDomTools({ allowActions: true })
      .registerCustomProperty("test", "name", "first");
    const first = builder.build();

    first.customProperties!.test.name = "mutated";
    first.domTools = false;

    expect(builder.build()).toMatchObject({
      toolGuard: "fullAccess",
      customProperties: { test: { name: "first" } },
      domTools: { allowActions: true },
    });
  });

  it("configures native tools and capture independently", () => {
    const options = createOptionsBuilder()
      .withFileSystemTools({ roots: [{ id: "cache", path: "cache" }] })
      .withDatabaseTools({ includePlatformRoots: true })
      .withPreferencesTools({ allowedKeys: ["theme"] })
      .withReflectionTools({ roots: [{ id: "app", label: "App" }] })
      .withoutFramesPerSecond()
      .withoutSessionJpegCapture()
      .withoutTouchCapture()
      .build();

    expect(options.enableFramesPerSecond).toBe(false);
    expect(options.sessionJpegCapture).toBe(false);
    expect(options.touchCapture).toBe(false);
    expect(options.remoteTools).toMatchObject({
      fileSystem: { roots: [{ id: "cache", path: "cache" }] },
      database: { includePlatformRoots: true },
      preferences: { allowedKeys: ["theme"] },
      reflection: { roots: [{ id: "app", label: "App" }] },
    });
  });

  it("supports the React Native-compatible builder conveniences", () => {
    const options = createOptionsBuilder()
      .withDefaultMemoryChannels({ managedHeap: true, rss: true })
      .withoutDefaultMemoryChannels({ managedHeap: true })
      .withSessionJpegCapture(
        1500,
        75,
        null,
        false,
        "screenshotWithVisualTreeOnTouch",
      )
      .withHostConnection({ savedConfigKey: "harness" })
      .configureHostConnection((connection) => {
        connection.discoveryPort = 4567;
      })
      .withBundledHostConnection({ bundledConfigJson: '{"kind":"test"}' })
      .withCellularHostConnections()
      .withHostConnectionProfileRetentionSeconds(90)
      .withVisualTreeTools()
      .withoutVisualTreeTools()
      .build();

    expect(options.defaultMemoryChannels).toMatchObject({
      managedHeap: false,
      rss: true,
    });
    expect(options.sessionJpegCapture).toEqual({
      intervalMilliseconds: 1500,
      quality: 75,
      maxWidth: null,
      captureGpuBackedSurfaces: false,
      mode: "screenshotWithVisualTreeOnTouch",
    });
    expect(options.hostConnection).toEqual({
      savedConfigKey: "harness",
      discoveryPort: 4567,
      allowCellularConnections: true,
      connectionProfileRetentionSeconds: 90,
      bundledConfigJson: '{"kind":"test"}',
    });
    expect(options.remoteTools?.visualTree).toBe(false);
  });

  it("configures and disables durable crash capture", () => {
    expect(
      createOptionsBuilder()
        .withCrashCapture({
          maximumPendingReports: 12,
          retentionDays: 3,
        })
        .build().crashCapture,
    ).toEqual({
      enabled: true,
      maximumPendingReports: 12,
      retentionDays: 3,
    });
    expect(
      createOptionsBuilder().withoutCrashCapture().build().crashCapture,
    ).toBe(false);
  });
});
