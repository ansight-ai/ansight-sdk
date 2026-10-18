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
    get: (_target, name) => async (...args) => {
      const options = args[0];
      if (name === "publishArtifact") { calls.push(args); return { success: false, message: "host refused" }; }
      if (name === "initialize" || name === "initializeAndActivate") calls.push(options);
      if (name === "presentAnnotation") {
        calls.push("presentAnnotation");
        return { status: "queued", annotationId: "sample", isSuccess: true };
      }
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


test("publishArtifact sends app metadata and payload without a tool request and preserves host failure", async () => {
  const calls = [];
  const runtime = loadRuntime(calls);
  const result = await runtime.publishArtifact({ metadata: {
    providerId: "scene", artifactId: "model", name: "Model", kind: "model",
    mimeType: "model/gltf-binary", fileName: "model.glb"
  }, payload: "glTF" });
  assert.equal(result.success, false);
  assert.equal(calls.length, 1);
  assert.equal(JSON.parse(calls[0][0]).providerId, "scene");
  assert.equal(calls[0][1], "Z2xURg==");
});
