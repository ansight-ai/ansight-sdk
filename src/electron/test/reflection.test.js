"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { installReflection } = require("../lib/reflection");
function setup() {
  const tools = new Map(),
    sdk = { registerTool: (d, h) => tools.set(d.id, { d, h }) };
  installReflection(sdk);
  return { sdk, tools, call: (id, a = {}) => tools.get(`reflect.${id}`).h(a) };
}
test("registered roots expose bounded live state without executing getters", () => {
  const { sdk, call } = setup();
  let reads = 0;
  const value = {
    count: 2,
    get secret() {
      reads++;
      throw new Error("getter executed");
    },
  };
  value.self = value;
  const unregister = sdk.registerRoot("app", value, {
    displayName: "App state",
  });
  const result = call("inspect_object", { root: "app" });
  assert.equal(reads, 0);
  assert.equal(
    result.snapshot.members.find((m) => m.name === "secret").value.evaluated,
    false,
  );
  assert.equal(
    result.snapshot.members.find((m) => m.name === "self").value.circular,
    true,
  );
  assert.throws(
    () => call("inspect_object", { root: "app", path: "secret" }),
    /Accessor/,
  );
  value.count = 3;
  assert.equal(
    call("inspect_object", { root: "app", path: "count" }).snapshot.value,
    3,
  );
  unregister();
  assert.throws(() => call("inspect_object", { root: "app" }), /Unknown/);
});
test("critical mutation operates on actual object and preserves method receiver", async () => {
  const { sdk, call, tools } = setup();
  const value = {
    count: 1,
    add(n) {
      this.count += n;
      return this.count;
    },
  };
  sdk.registerRoot("app", value);
  for (const id of ["set_member_value", "invoke_method"])
    assert.equal(tools.get(`reflect.${id}`).d.policy, "critical");
  call("set_member_value", { root: "app", path: "count", valueJson: "4" });
  assert.equal(value.count, 4);
  const result = await call("invoke_method", {
    root: "app",
    method: "add",
    argumentsJson: "[3]",
  });
  assert.equal(result.returnSnapshot.value, 7);
  assert.equal(value.count, 7);
  assert.equal(
    call("describe_type", { root: "app" }).methods.some(
      (m) => m.name === "add",
    ),
    true,
  );
  assert.throws(
    () =>
      call("set_member_value", {
        root: "app",
        path: "__proto__.polluted",
        valueJson: "true",
      }),
    /reserved/,
  );
  await assert.rejects(
    call("invoke_method", { root: "app", method: "constructor" }),
    /Invalid/,
  );
});
test("proxy traps and frozen runtime roots cannot be used to execute reads or overwrite state", () => {
  const { sdk, call } = setup();
  let traps = 0;
  const proxy = new Proxy(
    {},
    {
      ownKeys() {
        traps++;
        return [];
      },
    },
  );
  assert.throws(() => sdk.registerRoot("proxy", proxy), /Proxy/);
  assert.equal(traps, 0);
  assert.throws(
    () =>
      call("set_member_value", {
        root: "electron.runtime",
        path: "platform",
        valueJson: '"other"',
      }),
    /writable/,
  );
  assert.throws(
    () => call("inspect_object", { root: "electron.runtime", maxDepth: 999 }),
    /limits/,
  );
});
