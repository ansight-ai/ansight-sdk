"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  { installUiQuery } = require("../lib/ui-query");
function setup(truncated = false) {
  const windows = [{}, {}],
    tree = {
      source: "electron-dom",
      types: ["button"],
      root: {
        id: "n",
        typeId: 0,
        automationId: "save",
        role: "button",
        label: "Save",
        visible: true,
        enabled: true,
        supportedActions: ["tap"],
        children: [],
      },
      truncated,
    };
  const tools = new Map([
    ...["ui.get_visual_tree", "dom.get_document"].map((id) => [
      id,
      { handler: async () => structuredClone(tree) },
    ]),
    [
      "ui.perform_action",
      {
        descriptor: { argumentsSchema: { properties: {} } },
        handler: async () => ({ performed: true }),
      },
    ],
  ]);
  const sdk = {
    tools,
    window: (a) => windows[a.windowId ?? 0],
    runDom: async () => structuredClone(tree),
    registerTool: (d, h) => tools.set(d.id, { descriptor: d, handler: h }),
  };
  installUiQuery(sdk);
  return { call: (id, a = {}) => tools.get(id).handler(a) };
}
test("references cannot cross windows or survive a newer capture", async () => {
  const { call } = setup();
  const result = await call("ui.query_nodes", { automationId: "save" }),
    reference = result.matches[0].reference;
  await assert.rejects(
    call("ui.inspect_node", { windowId: 1, reference }),
    /superseded/,
  );
  await call("dom.get_document", {});
  await assert.rejects(
    call("ui.perform_action", { reference, action: "tap" }),
    /superseded/,
  );
});
test("absence cannot be proved from truncated capture", async () => {
  const { call } = setup(true);
  await assert.rejects(
    call("ui.wait", {
      automationId: "missing",
      condition: "notExists",
      timeoutMilliseconds: 15,
      pollMilliseconds: 10,
    }),
    /timed out/,
  );
});
test("queries apply combined filters and reject unregistered providers", async () => {
  const { call } = setup();
  assert.equal(
    (
      await call("ui.query_nodes", {
        automationId: "save",
        type: "BUTTON",
        textContains: "SAVE",
        action: "tap",
        visible: true,
      })
    ).count,
    1,
  );
  assert.equal(
    (await call("ui.query_nodes", { automationId: "save", enabled: false }))
      .count,
    0,
  );
  await assert.rejects(
    call("ui.query_nodes", { source: "unregistered" }),
    /Unknown/,
  );
});
