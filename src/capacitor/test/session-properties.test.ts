import { describe, expect, it } from "vitest";

import {
  ANSIGHT_CAPACITOR_SDK_VERSION,
  COMPILED_CAPACITOR_CORE_VERSION,
  createAutomaticSessionProperties,
  mergeSessionProperties,
} from "../src/session-properties";

describe("automatic session properties", () => {
  it("captures the Capacitor and localization environment", () => {
    const properties = createAutomaticSessionProperties({
      platform: "android",
      nativePlatform: true,
      userAgent:
        "Mozilla/5.0 (Linux; Android 16; wv) AppleWebKit/537.36 " +
        "Chrome/140.0.7339.51 Mobile Safari/537.36",
      locale: "en_AU",
      timeZone: "Australia/Sydney",
      utcOffsetMinutes: 600,
    });

    expect(properties.capacitor).toEqual({
      sdkVersion: ANSIGHT_CAPACITOR_SDK_VERSION,
      capacitorVersion: "8.x",
      compiledCapacitorVersion: COMPILED_CAPACITOR_CORE_VERSION,
      platform: "android",
      runtimeLanguage: "javascript",
      executionMode: "native",
      webViewEngine: "chromiumWebView",
      webViewEngineVersion: "140.0.7339.51",
      userAgent:
        "Mozilla/5.0 (Linux; Android 16; wv) AppleWebKit/537.36 " +
        "Chrome/140.0.7339.51 Mobile Safari/537.36",
    });
    expect(properties.localization).toEqual({
      locale: "en-AU",
      language: "en",
      region: "AU",
      timeZone: "Australia/Sydney",
      utcOffsetMinutes: "600",
    });
  });

  it("preserves defaults while applying caller overrides", () => {
    expect(
      mergeSessionProperties(
        {
          capacitor: { sdkVersion: "1.0", platform: "ios" },
          localization: { language: "en" },
        },
        {
          capacitor: { platform: "test" },
          app: { tenant: "acme" },
        },
      ),
    ).toEqual({
      capacitor: { sdkVersion: "1.0", platform: "test" },
      localization: { language: "en" },
      app: { tenant: "acme" },
    });
  });
});
