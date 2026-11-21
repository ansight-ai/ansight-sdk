"use strict";
const { randomUUID } = require("node:crypto"),
  { inspectDom } = require("./dom");
const aliases = new Set([
  "native",
  "dom",
  "electron-dom",
  "dom.get_document",
  "ui.get_visual_tree",
]);
function source(value) {
  if (value != null && !aliases.has(value))
    throw new Error("Unknown visual-tree source");
  return "electron-dom";
}
function stale(message) {
  const error = new Error(message);
  error.code = "ui_snapshot_stale";
  throw error;
}
function flatten(root, ancestors = [], out = []) {
  if (root) {
    out.push({ node: root, ancestors });
    for (const child of root.children || [])
      flatten(child, [...ancestors, root], out);
  }
  return out;
}
function bounded(value, fallback, min, max, name) {
  const n = value ?? fallback;
  if (!Number.isInteger(n) || n < min || n > max)
    throw new Error(`Invalid ${name}`);
  return n;
}
function installUiQuery(sdk) {
  const latest = new WeakMap();
  let revision = 0;
  const originalCapture = sdk.tools.get("ui.get_visual_tree").handler;
  const capture = async (a) => {
    source(a.source);
    const w = sdk.window(a),
      tree = await originalCapture(a);
    const r = ++revision;
    Object.assign(tree, {
      source: "electron-dom",
      snapshotId: `electron-dom:${r}:${randomUUID()}`,
      revision: r,
      nodeIdentity: {
        scope: "snapshot",
        source: "electron-dom",
        staleAfterRevision: r,
      },
    });
    latest.set(w, tree);
    return tree;
  };
  for (const id of ["ui.get_visual_tree", "dom.get_document"])
    sdk.tools.get(id).handler = capture;
  const current = (a) => {
    source(a.source);
    const tree = latest.get(sdk.window(a));
    if (!tree || tree.snapshotId !== a.snapshotId)
      stale("UI snapshot is unknown or superseded; query a fresh tree");
    return tree;
  };
  const reference = (tree, node) => ({
    source: tree.source,
    snapshotId: tree.snapshotId,
    nodeId: node.id,
  });
  const query = async (a) => {
    if (a.root) throw new Error("DOM root scoping uses rootNodeId");
    const max = bounded(a.maxResults, 50, 1, 500, "maxResults");
    const tree = a.snapshotId ? current(a) : await capture(a);
    let root = tree.root;
    if (a.rootNodeId) {
      root = flatten(root).find((x) => x.node.id === a.rootNodeId)?.node;
      if (!root) stale("Subtree node no longer exists");
    }
    const matches = flatten(root)
      .map((x) => x.node)
      .filter(
        (n) =>
          (!a.nodeId || n.id === a.nodeId) &&
          (!a.automationId || n.automationId === a.automationId) &&
          (!a.role || n.role === a.role) &&
          (!a.type ||
            String(tree.types[n.typeId])
              .toLowerCase()
              .includes(a.type.toLowerCase())) &&
          (!a.textContains ||
            [n.label, n.visual?.text, n.visual?.value]
              .filter(Boolean)
              .join(" ")
              .toLowerCase()
              .includes(a.textContains.toLowerCase())) &&
          (!a.action || n.supportedActions?.includes(a.action)) &&
          (a.visible == null || n.visible === a.visible) &&
          (a.enabled == null || n.enabled === a.enabled),
      );
    return {
      source: tree.source,
      snapshotId: tree.snapshotId,
      revision: tree.revision,
      count: Math.min(max, matches.length),
      totalMatches: matches.length,
      truncated: matches.length > max || tree.truncated,
      matches: matches.slice(0, max).map((n) => {
        const { children, ...node } = n;
        return {
          ...node,
          type: tree.types[n.typeId],
          reference: reference(tree, n),
        };
      }),
    };
  };
  const str = { type: "string" },
    bool = { type: "boolean" },
    integer = { type: "integer" };
  const filters = {
    windowId: integer,
    source: str,
    snapshotId: str,
    nodeId: str,
    automationId: str,
    role: str,
    type: str,
    textContains: str,
    action: str,
    visible: bool,
    enabled: bool,
    maxResults: integer,
    root: str,
    rootNodeId: str,
    maxDepth: integer,
    maxNodes: integer,
    includeBounds: bool,
    includeComputedStyles: bool,
    includeProperties: bool,
    includeInactivePages: bool,
  };
  const add = (id, properties, required, handler) =>
    sdk.registerTool(
      {
        id,
        policy: "read",
        category: "ui",
        description:
          "Snapshot-scoped inspection of the explicitly attached Electron DOM, with native Ansight UI query semantics.",
        argumentsSchema: {
          type: "object",
          properties,
          required,
          additionalProperties: false,
        },
      },
      handler,
    );
  add("ui.query_nodes", filters, [], query);
  add(
    "ui.inspect_node",
    {
      windowId: integer,
      source: str,
      snapshotId: str,
      nodeId: str,
      reference: { type: "object" },
      includeAncestors: bool,
      includeDescendants: bool,
      includeProperties: bool,
    },
    [],
    async (a) => {
      const args = { ...a.reference, ...a };
      if (typeof args.nodeId !== "string")
        throw new Error("nodeId is required");
      const tree = args.snapshotId ? current(args) : await capture(args);
      if (!flatten(tree.root).some((x) => x.node.id === args.nodeId))
        stale("Node does not belong to the referenced snapshot");
      const live = await sdk.runDom(sdk.window(args), inspectDom, {
          includeProperties: a.includeProperties === true,
          includeInactivePages: true,
        }),
        entry = flatten(live.root).find((x) => x.node.id === args.nodeId);
      if (!entry) stale("Node is no longer in the live DOM");
      const strip = (n) => {
        const { children, ...value } = n;
        return value;
      };
      return {
        format: live.format,
        platform: live.platform,
        source: tree.source,
        adapter: live.adapter,
        capturedAtUtc: live.capturedAtUtc,
        snapshotId: tree.snapshotId,
        revision: tree.revision,
        reference: reference(tree, entry.node),
        types: live.types,
        node: a.includeDescendants ? entry.node : strip(entry.node),
        ancestors: a.includeAncestors ? entry.ancestors.map(strip) : null,
        descendants: a.includeDescendants
          ? flatten(entry.node)
              .slice(1)
              .map((x) => strip(x.node))
          : null,
      };
    },
  );
  add(
    "ui.wait",
    {
      ...filters,
      condition: {
        type: "string",
        enum: ["exists", "notExists", "visible", "enabled"],
      },
      timeoutMilliseconds: integer,
      pollMilliseconds: integer,
    },
    ["condition"],
    async (a) => {
      if (!["exists", "notExists", "visible", "enabled"].includes(a.condition))
        throw new Error("Invalid wait condition");
      const timeout = bounded(
          a.timeoutMilliseconds,
          5000,
          1,
          60000,
          "timeoutMilliseconds",
        ),
        poll = bounded(a.pollMilliseconds, 100, 10, 5000, "pollMilliseconds"),
        start = performance.now();
      while (true) {
        const args = { ...a, snapshotId: undefined };
        if (a.condition === "visible") args.visible = true;
        if (a.condition === "enabled") args.enabled = true;
        const result = await query(args),
          elapsed = Math.round(performance.now() - start);
        const matched =
          a.condition === "notExists"
            ? result.totalMatches === 0 && !result.truncated
            : result.totalMatches > 0;
        if (matched)
          return {
            condition: a.condition,
            matched: true,
            elapsedMilliseconds: elapsed,
            query: result,
          };
        if (elapsed >= timeout) {
          const e = new Error(
            "UI wait timed out without satisfying the condition",
          );
          e.code = "ui_wait_timeout";
          throw e;
        }
        await new Promise((resolve) =>
          setTimeout(resolve, Math.min(poll, timeout - elapsed)),
        );
      }
    },
  );
  const action = sdk.tools.get("ui.perform_action"),
    originalAction = action.handler;
  Object.assign(action.descriptor.argumentsSchema.properties, {
    source: str,
    snapshotId: str,
    reference: { type: "object" },
  });
  action.handler = async (a) => {
    const args = { ...a.reference, ...a };
    let tree;
    if (args.snapshotId) {
      tree = current(args);
      if (!flatten(tree.root).some((x) => x.node.id === args.nodeId))
        stale("Node does not belong to the referenced snapshot");
    }
    const result = await originalAction(args);
    return {
      ...result,
      source: "electron-dom",
      action: args.action,
      ...(tree ? { reference: reference(tree, { id: args.nodeId }) } : {}),
    };
  };
}
module.exports = { installUiQuery };
