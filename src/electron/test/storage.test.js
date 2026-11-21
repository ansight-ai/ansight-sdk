"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  vm = require("node:vm");
const { installStorage } = require("../lib/storage");
function webStorage() {
  const s = {};
  Object.defineProperties(s, {
    getItem: { value: (k) => (Object.hasOwn(s, k) ? s[k] : null) },
    setItem: {
      value: (k, v) => {
        Object.defineProperty(s, k, {
          value: String(v),
          writable: true,
          enumerable: true,
          configurable: true,
        });
      },
    },
    removeItem: {
      value: (k) => {
        delete s[k];
      },
    },
  });
  return s;
}
function setup(secureStorage) {
  const tools = new Map();
  let clipboard = null;
  const context = vm.createContext({
    localStorage: webStorage(),
    sessionStorage: webStorage(),
    TextEncoder,
  });
  const sdk = {
    options: { secureStorage },
    registerTool: (d, h) => tools.set(d.id, { d, h }),
    window: () => ({}),
    runDom: async (w, fn, a) =>
      vm.runInContext(`(${fn.toString()})(${JSON.stringify(a)})`, context),
    electron: {
      clipboard: {
        availableFormats: () => (clipboard === null ? [] : ["text/plain"]),
        readText: () => clipboard,
        writeText: (t) => {
          clipboard = t;
        },
        clear: () => {
          clipboard = null;
        },
      },
    },
  };
  installStorage(sdk);
  return { tools, call: (id, a = {}) => tools.get(id).h(a) };
}
test("preferences operate on real Web Storage values, separate stores, and bounded key listings", async () => {
  const { call } = setup();
  await call("prefs.set_value", {
    key: "lex-theme",
    value: "dark",
    valueType: "string",
  });
  assert.equal(
    (await call("prefs.get_value", { key: "lex-theme" })).value,
    "dark",
  );
  assert.equal(
    (
      await call("prefs.get_value", {
        key: "lex-theme",
        store: "sessionStorage",
      })
    ).exists,
    false,
  );
  await call("prefs.set_value", {
    key: "lex-test",
    value: "123",
    valueType: "string",
  });
  const list = await call("prefs.list_keys", { prefix: "lex-", maxResults: 1 });
  assert.equal(list.keys.length, 1);
  assert.equal(list.truncated, true);
  assert.equal(
    (await call("prefs.remove_key", { key: "lex-test" })).removed,
    true,
  );
  assert.equal(
    (await call("prefs.remove_key", { key: "lex-test" })).removed,
    false,
  );
  await assert.rejects(
    call("prefs.set_value", { key: "x", value: "1", valueType: "integer" }),
    /string values/,
  );
  await assert.rejects(
    call("prefs.get_value", { key: "x", store: "arbitrary" }),
    /Unknown/,
  );
});
test("clipboard preserves native empty-text semantics and rejects oversize UTF8", () => {
  const { call } = setup();
  assert.equal(call("clipboard.get_text").text, null);
  call("clipboard.set_text", { text: "" });
  assert.equal(call("clipboard.get_text").hasText, true);
  call("clipboard.set_text", { text: "Résumé ✓" });
  assert.equal(call("clipboard.get_text").text, "Résumé ✓");
  assert.throws(
    () => call("clipboard.set_text", { text: "😀".repeat(20000) }),
    /65536/,
  );
  assert.equal(call("clipboard.get_text").text, "Résumé ✓");
  call("clipboard.clear");
  assert.equal(call("clipboard.has_text").hasText, false);
});
test("secure tools use only the registered backend and retain critical policy", async () => {
  assert.equal(setup().tools.has("secure.get_value"), false);
  const map = new Map();
  const { tools, call } = setup({
    store: "app-vault",
    get: (k) => map.get(k),
    set: (k, v) => map.set(k, v),
    remove: (k) => map.delete(k),
  });
  for (const id of [
    "secure.get_value",
    "secure.set_value",
    "secure.remove_key",
    "prefs.remove_key",
  ])
    assert.equal(tools.get(id).d.policy, "critical");
  await call("secure.set_value", { key: "fixture", value: "test-only" });
  assert.equal(map.get("fixture"), "test-only");
  assert.equal(
    (await call("secure.get_value", { key: "fixture" })).value,
    "test-only",
  );
  assert.equal(
    (await call("secure.remove_key", { key: "fixture" })).removed,
    true,
  );
  assert.equal(
    (await call("secure.get_value", { key: "fixture" })).exists,
    false,
  );
});
