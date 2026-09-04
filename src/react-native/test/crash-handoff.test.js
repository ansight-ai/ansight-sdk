"use strict";
const assert = require("node:assert/strict");
const test = require("node:test");
const vm = require("node:vm");
const fs = require("node:fs");
const { createRequire } = require("node:module");
const entryPath = require.resolve("../index.js");
const localRequire = createRequire(entryPath);

function loadRuntime(calls) {
  const native = new Proxy({}, {
    get: (_target, name) => async (options) => {
      if (name === "initialize" || name === "initializeAndActivate") calls.push(options);
      return {};
    },
  });
  const context = {
    module: { exports: {} },
    __DEV__: true,
    global: {},
    console,
    setTimeout,
    clearTimeout,
    require(name) {
      if (name === "react") return { version: "19.1.0" };
      if (name === "react-native") return {
        NativeModules: { AnsightReactNative: native },
        Platform: { OS: "ios" },
        NativeEventEmitter: class { addListener() { return { remove() {} }; } },
      };
      return localRequire(name);
    },
  };
  vm.runInNewContext(fs.readFileSync(entryPath, "utf8"), context, { filename: entryPath });
  return context.module.exports;
}

for (const method of ["initialize", "initializeAndActivate"]) {
  test(`${method} forwards explicit host handoff settings to native`, async () => {
    const calls = [];
    const runtime = loadRuntime(calls);
    for (const crashCapture of [
      { hostHandoffEnabled: false },
      { hostHandoffEnabled: true },
    ]) {
      await runtime[method]({ crashCapture, lifecycle: false, networkCapture: false });
      assert.equal(calls.at(-1).crashCapture.hostHandoffEnabled, crashCapture.hostHandoffEnabled);
    }
    await runtime[method]({ crashCapture: false, lifecycle: false, networkCapture: false });
    assert.equal(calls.at(-1).crashCapture, false);
  });
}
