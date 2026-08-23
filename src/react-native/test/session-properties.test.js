"use strict";

const assert = require("node:assert/strict");
const test = require("node:test");

const {
  createAutomaticSessionProperties,
  formatReactNativeVersion,
  mergeSessionProperties,
} = require("../session-properties");

test("formats React Native versions", () => {
  assert.equal(
    formatReactNativeVersion({ major: 0, minor: 82, patch: 1, prerelease: "rc.0" }),
    "0.82.1-rc.0"
  );
});

test("captures React Native runtime properties", () => {
  const properties = createAutomaticSessionProperties({
    platform: {
      OS: "ios",
      constants: { reactNativeVersion: { major: 0, minor: 82, patch: 1 } },
    },
    reactVersion: "19.1.1",
    runtimeGlobal: {
      HermesInternal: {
        getRuntimeProperties: () => ({
          "OSS Release Version": "0.13.0",
          "Bytecode Version": 96,
        }),
      },
      nativeFabricUIManager: {},
      RN$Bridgeless: true,
    },
    developmentMode: true,
  });

  assert.deepEqual(properties.reactNative, {
    sdkVersion: require("../package.json").version,
    platform: "ios",
    runtimeLanguage: "javascript",
    javascriptEngine: "hermes",
    architecture: "new",
    newArchitectureEnabled: "true",
    bridgelessEnabled: "true",
    developmentMode: "true",
    reactNativeVersion: "0.82.1",
    reactVersion: "19.1.1",
    javascriptEngineVersion: "0.13.0",
    hermesBytecodeVersion: "96",
  });
  assert.match(properties.localization.locale, /^[A-Za-z]{2,3}(?:-|$)/);
  assert.ok(properties.localization.language);
  assert.ok("utcOffsetMinutes" in properties.localization);
});

test("caller properties override automatic values without dropping defaults", () => {
  assert.deepEqual(
    mergeSessionProperties(
      {
        reactNative: { sdkVersion: "1.0", javascriptEngine: "hermes" },
        localization: { language: "en", timeZone: "Australia/Sydney" },
      },
      {
        reactNative: { javascriptEngine: "test-engine" },
        app: { tenant: "acme" },
      }
    ),
    {
      reactNative: { sdkVersion: "1.0", javascriptEngine: "test-engine" },
      localization: { language: "en", timeZone: "Australia/Sydney" },
      app: { tenant: "acme" },
    }
  );
});
