"use strict";
const { types } = require("node:util");
const forbidden = new Set(["__proto__", "prototype", "constructor"]);
function checked(value) {
  if (
    value !== null &&
    (typeof value === "object" || typeof value === "function") &&
    types.isProxy(value)
  )
    throw new Error(
      "Proxy objects cannot be inspected without executing app traps",
    );
  return value;
}
function typeName(value) {
  checked(value);
  if (value === null) return "null";
  if (typeof value !== "object") return typeof value;
  const proto = Object.getPrototypeOf(value);
  checked(proto);
  const ctor = Object.getOwnPropertyDescriptor(
    proto || {},
    "constructor",
  )?.value;
  checked(ctor);
  return typeof ctor === "function"
    ? Object.getOwnPropertyDescriptor(ctor, "name")?.value || "Object"
    : "Object";
}
function parts(path) {
  if (path == null || path === "") return [];
  if (typeof path !== "string" || path.length > 1024)
    throw new Error("Invalid member path");
  const keys = path.replace(/\[(\d+)\]/g, ".$1").split(".");
  if (keys.some((k) => !k || forbidden.has(k)))
    throw new Error("Invalid or reserved member path");
  return keys;
}
function descriptor(value, key) {
  checked(value);
  if (value === null || !["object", "function"].includes(typeof value))
    throw new Error("Target is not an object");
  let current = value;
  for (
    let depth = 0;
    current && depth < 10;
    depth++, current = Object.getPrototypeOf(current)
  ) {
    checked(current);
    const d = Object.getOwnPropertyDescriptor(current, key);
    if (d) return d;
  }
  throw new Error(`Member not found: ${key}`);
}
function resolve(value, path) {
  for (const key of parts(path)) {
    const d = descriptor(value, key);
    if (!Object.hasOwn(d, "value"))
      throw new Error(
        "Accessor evaluation is not permitted by inspection tools",
      );
    value = d.value;
  }
  return checked(value);
}
function snapshot(
  value,
  depth = 2,
  limit = 50,
  seen = new WeakSet(),
  budget = { nodes: 1000 },
) {
  checked(value);
  if (--budget.nodes < 0) return { kind: "truncated" };
  const type = typeName(value);
  if (
    value === null ||
    ["string", "number", "boolean", "undefined", "bigint", "symbol"].includes(
      typeof value,
    )
  )
    return {
      type,
      kind: "value",
      value:
        typeof value === "string"
          ? value.slice(0, 4096)
          : typeof value === "bigint" || typeof value === "symbol"
            ? String(value)
            : (value ?? null),
    };
  if (typeof value === "function")
    return {
      type,
      kind: "method",
      name: Object.getOwnPropertyDescriptor(value, "name")?.value || "",
    };
  if (seen.has(value)) return { type, kind: "reference", circular: true };
  seen.add(value);
  if (depth === 0) return { type, kind: "object", truncated: true };
  const keys = Object.getOwnPropertyNames(value),
    members = [];
  for (const name of keys.slice(0, limit)) {
    const d = Object.getOwnPropertyDescriptor(value, name);
    members.push({
      name,
      writable: !!d.writable,
      kind: Object.hasOwn(d, "value") ? "field" : "accessor",
      value: Object.hasOwn(d, "value")
        ? snapshot(d.value, depth - 1, limit, seen, budget)
        : { kind: "accessor", evaluated: false },
    });
  }
  return {
    type,
    kind: Array.isArray(value) ? "array" : "object",
    members,
    truncated: keys.length > limit || budget.nodes < 0,
  };
}
function installReflection(sdk) {
  const roots = new Map();
  sdk.registerRoot = (id, value, metadata = {}) => {
    if (typeof id !== "string" || !id || id.length > 256 || roots.has(id))
      throw new Error("Invalid or duplicate reflection root");
    checked(value);
    const safeMetadata = {};
    for (const key of ["displayName", "description"]) {
      const field = Object.getOwnPropertyDescriptor(metadata, key);
      if (field) {
        if (typeof field.value !== "string" || field.value.length > 4096)
          throw new Error("Invalid root metadata");
        safeMetadata[key] = field.value;
      }
    }
    const hints = Object.getOwnPropertyDescriptor(metadata, "hints");
    if (hints) {
      const values = checked(hints.value);
      if (!Array.isArray(values) || values.length > 32)
        throw new Error("Invalid root hints");
      safeMetadata.hints = [];
      for (let i = 0; i < values.length; i++) {
        const hint = Object.getOwnPropertyDescriptor(values, String(i));
        if (typeof hint?.value !== "string" || hint.value.length > 256)
          throw new Error("Invalid root hint");
        safeMetadata.hints.push(hint.value);
      }
    }
    roots.set(id, { value, metadata: safeMetadata });
    return () => roots.delete(id);
  };
  sdk.registerRoot(
    "electron.runtime",
    Object.freeze({
      platform: process.platform,
      architecture: process.arch,
      versions: Object.freeze({ ...process.versions }),
    }),
    { displayName: "Electron runtime" },
  );
  const root = (a) => {
    const entry = roots.get(a.root);
    if (!entry) throw new Error("Unknown registered root");
    return entry.value;
  };
  const add = (id, policy, properties, required, handler) =>
    sdk.registerTool(
      {
        id: `reflect.${id}`,
        category: "reflect",
        policy,
        description:
          "Inspect explicitly registered JavaScript roots. Read tools never invoke getters; writes and method calls require critical policy.",
        argumentsSchema: {
          type: "object",
          properties,
          required,
          additionalProperties: false,
        },
      },
      (a) => {
        const bound = (result) => {
          if (Buffer.byteLength(JSON.stringify(result)) > 1048576)
            throw new Error(
              "Reflection response exceeds 1 MiB; reduce inspection limits",
            );
          return result;
        };
        const result = handler(a);
        return result instanceof Promise ? result.then(bound) : bound(result);
      },
    );
  const str = { type: "string" },
    base = { root: str, path: str };
  add("list_roots", "read", {}, [], () => ({
    roots: [...roots].map(([id, r]) => ({
      id,
      metadata: { displayName: id, ...r.metadata },
      hostRuntime: {
        kind: "javascript",
        displayName: "Electron main process",
        platform: process.platform,
        engine: "V8",
      },
      referenceType: "strong",
      available: true,
      runtimeType: typeName(r.value),
      type: typeName(r.value),
      memberVisibility: "own-properties; getters not evaluated",
    })),
    count: roots.size,
    capturedAtUtc: new Date().toISOString(),
  }));
  add(
    "inspect_object",
    "read",
    {
      ...base,
      maxDepth: { type: "integer", minimum: 0, maximum: 5 },
      maxItemsPerCollection: { type: "integer", minimum: 1, maximum: 100 },
    },
    ["root"],
    (a) => {
      const depth = a.maxDepth ?? 2,
        limit = a.maxItemsPerCollection ?? 50;
      if (
        !Number.isInteger(depth) ||
        depth < 0 ||
        depth > 5 ||
        !Number.isInteger(limit) ||
        limit < 1 ||
        limit > 100
      )
        throw new Error("Invalid inspection limits");
      return {
        root: a.root,
        path: a.path ?? null,
        snapshot: snapshot(resolve(root(a), a.path), depth, limit),
        capturedAtUtc: new Date().toISOString(),
      };
    },
  );
  add("describe_type", "read", base, ["root"], (a) => {
    const value = resolve(root(a), a.path),
      members = [],
      methods = [];
    let current = value;
    const seen = new Set();
    for (
      let depth = 0;
      current && depth < 10;
      depth++, current = Object.getPrototypeOf(current)
    ) {
      checked(current);
      for (const name of Object.getOwnPropertyNames(current)) {
        if (seen.has(name) || forbidden.has(name)) continue;
        seen.add(name);
        if (seen.size > 200) break;
        const d = Object.getOwnPropertyDescriptor(current, name);
        const entry = {
          name,
          kind: Object.hasOwn(d, "value") ? "field" : "accessor",
          writable: !!d.writable,
        };
        if (typeof d.value === "function")
          methods.push({ ...entry, kind: "method" });
        else members.push(entry);
      }
      if (seen.size > 200) break;
    }
    return {
      typeName: typeName(value),
      assemblyName: "JavaScript",
      namespace: null,
      kind: typeof value,
      baseType: null,
      interfaces: [],
      genericArity: 0,
      memberVisibility: "own and prototype descriptors; getters not evaluated",
      members,
      methods,
      truncated: seen.size > 200,
      capturedAtUtc: new Date().toISOString(),
    };
  });
  add(
    "set_member_value",
    "critical",
    { ...base, valueJson: str },
    ["root", "path", "valueJson"],
    (a) => {
      const keys = parts(a.path);
      if (!keys.length) throw new Error("A member path is required");
      const key = keys.pop(),
        target = resolve(root(a), keys.join(".")),
        d = Object.getOwnPropertyDescriptor(target, key);
      if (!d || !Object.hasOwn(d, "value") || !d.writable)
        throw new Error(
          "Member must be an existing writable own data property",
        );
      if (
        typeof a.valueJson !== "string" ||
        Buffer.byteLength(a.valueJson) > 65536
      )
        throw new Error("Invalid valueJson");
      const value = JSON.parse(a.valueJson);
      Object.defineProperty(target, key, { ...d, value });
      return {
        root: a.root,
        path: a.path,
        updated: true,
        snapshot: snapshot(value),
        capturedAtUtc: new Date().toISOString(),
      };
    },
  );
  add(
    "invoke_method",
    "critical",
    { root: str, targetPath: str, method: str, argumentsJson: str },
    ["root", "method"],
    async (a) => {
      if (typeof a.method !== "string" || forbidden.has(a.method))
        throw new Error("Invalid method name");
      const target = resolve(root(a), a.targetPath),
        d = descriptor(target, a.method);
      if (typeof d.value !== "function")
        throw new Error("Member is not a data-property method");
      if (
        a.argumentsJson !== undefined &&
        (typeof a.argumentsJson !== "string" ||
          Buffer.byteLength(a.argumentsJson) > 65536)
      )
        throw new Error("Invalid argumentsJson");
      const args = JSON.parse(a.argumentsJson ?? "[]");
      if (!Array.isArray(args) || args.length > 32)
        throw new Error(
          "Method arguments must be an array of at most 32 entries",
        );
      const result = await Reflect.apply(d.value, target, args);
      return {
        root: a.root,
        targetPath: a.targetPath ?? null,
        signature: a.method,
        invoked: true,
        returnSnapshot: snapshot(result),
        capturedAtUtc: new Date().toISOString(),
      };
    },
  );
}
module.exports = { installReflection, snapshot };
