"use strict";
const string = { type: "string" };
function text(value, name, max = 65536) {
  if (typeof value !== "string" || Buffer.byteLength(value) > max)
    throw new Error(`${name} must be a string of at most ${max} UTF-8 bytes`);
  return value;
}
function rendererPreferences(a) {
  const storage = a.store === "sessionStorage" ? sessionStorage : localStorage;
  const capturedAtUtc = new Date().toISOString();
  const base = { store: a.store, capturedAtUtc };
  if (a.action === "list_keys") {
    const keys = Object.keys(storage)
      .filter((key) => key.startsWith(a.prefix || ""))
      .sort();
    return {
      ...base,
      keys: keys.slice(0, a.maxResults),
      truncated: keys.length > a.maxResults,
    };
  }
  const value = storage.getItem(a.key);
  if (a.action === "get_value") {
    if (value !== null && new TextEncoder().encode(value).length > 65536)
      throw new Error("Preference value exceeds 65536 UTF-8 bytes");
    return {
      ...base,
      key: a.key,
      exists: value !== null,
      value,
      valueType: value === null ? null : "string",
    };
  }
  if (a.action === "set_value") {
    storage.setItem(a.key, a.value);
    return { ...base, key: a.key, valueType: "string", updated: true };
  }
  storage.removeItem(a.key);
  return { ...base, key: a.key, removed: value !== null };
}
function installStorage(sdk) {
  const add = (id, policy, properties, required, description, handler) =>
    sdk.registerTool(
      {
        id,
        policy,
        category: id.split(".")[0],
        description,
        argumentsSchema: {
          type: "object",
          properties,
          required,
          additionalProperties: false,
        },
      },
      handler,
    );
  for (const action of ["list_keys", "get_value", "set_value", "remove_key"]) {
    const properties = {
      store: { type: "string", enum: ["localStorage", "sessionStorage"] },
      windowId: { type: "integer" },
    };
    if (action === "list_keys")
      Object.assign(properties, {
        prefix: string,
        maxResults: { type: "integer", minimum: 1, maximum: 1000 },
      });
    else properties.key = string;
    if (action === "set_value")
      Object.assign(properties, {
        value: string,
        valueType: { type: "string", enum: ["string"] },
      });
    const required =
      action === "list_keys"
        ? []
        : action === "set_value"
          ? ["key", "value", "valueType"]
          : ["key"];
    add(
      `prefs.${action}`,
      action === "set_value"
        ? "write"
        : action === "remove_key"
          ? "critical"
          : "read",
      properties,
      required,
      "Access the selected attached renderer's real Web Storage. Web Storage values are strings; changes follow browser storage semantics.",
      async (args) => {
        const a = { ...args, action, store: args.store ?? "localStorage" };
        if (!["localStorage", "sessionStorage"].includes(a.store))
          throw new Error("Unknown preferences store");
        if (action !== "list_keys") text(a.key, "key", 1024);
        else {
          if (a.prefix !== undefined) text(a.prefix, "prefix", 1024);
          a.maxResults ??= 200;
          if (
            !Number.isInteger(a.maxResults) ||
            a.maxResults < 1 ||
            a.maxResults > 1000
          )
            throw new Error("maxResults must be between 1 and 1000");
        }
        if (action === "set_value") {
          text(a.value, "value");
          if (a.valueType !== "string")
            throw new Error("Web Storage supports string values only");
        }
        return sdk.runDom(sdk.window(a), rendererPreferences, a);
      },
    );
  }
  for (const action of ["get_text", "has_text", "set_text", "clear"]) {
    add(
      `clipboard.${action}`,
      ["set_text", "clear"].includes(action) ? "write" : "read",
      action === "set_text" ? { text: string } : {},
      action === "set_text" ? ["text"] : [],
      "Access Electron's native system clipboard using the standard Ansight text contract.",
      (a) => {
        const board = sdk.electron.clipboard;
        if (!board) throw new Error("Native clipboard is unavailable");
        if (action === "has_text")
          return { hasText: board.availableFormats().includes("text/plain") };
        if (action === "get_text") {
          const hasText = board.availableFormats().includes("text/plain");
          return {
            hasText,
            text: hasText ? text(board.readText(), "clipboard text") : null,
          };
        }
        if (action === "set_text") {
          board.writeText(text(a.text, "text"));
          return { updated: true };
        }
        board.clear();
        return { cleared: true };
      },
    );
  }
  // Electron has no universal secure key/value store. Use the app-owned adapter,
  // never a second store that cannot observe the application's actual secrets.
  if (sdk.options.secureStorage) {
    const backend = sdk.options.secureStorage;
    for (const method of ["get", "set", "remove"])
      if (typeof backend[method] !== "function")
        throw new Error(`secureStorage.${method} is required`);
    text(backend.store, "secure storage name", 1024);
    for (const action of ["get_value", "set_value", "remove_key"]) {
      add(
        `secure.${action}`,
        "critical",
        action === "set_value"
          ? { key: string, value: string }
          : { key: string },
        action === "set_value" ? ["key", "value"] : ["key"],
        "Access the app-registered secure storage adapter; all operations require critical policy.",
        async (a) => {
          text(a.key, "key", 1024);
          const base = {
            store: backend.store,
            key: a.key,
            capturedAtUtc: new Date().toISOString(),
          };
          if (action === "get_value") {
            const value = await backend.get(a.key);
            return {
              ...base,
              exists: value != null,
              value: value == null ? null : text(value, "secure value"),
            };
          }
          if (action === "set_value") {
            await backend.set(a.key, text(a.value, "value"));
            return { ...base, updated: true };
          }
          return { ...base, removed: await backend.remove(a.key) };
        },
      );
    }
  }
}
module.exports = { installStorage, rendererPreferences };
