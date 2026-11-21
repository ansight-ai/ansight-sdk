"use strict";
const fs = require("node:fs/promises"),
  path = require("node:path"),
  { fork } = require("node:child_process");
function executeDatabase(
  modulePath,
  databasePath,
  operation,
  args,
  timeoutMs = 4000,
) {
  return new Promise((resolve, reject) => {
    const child = fork(path.join(__dirname, "database-worker.js"), [], {
      execPath: process.execPath,
      env: { ...process.env, ELECTRON_RUN_AS_NODE: "1" },
      execArgv: ["--max-old-space-size=64"],
      stdio: ["ignore", "ignore", "ignore", "ipc"],
    });
    let settled = false;
    const finish = (error, result) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      child.kill("SIGKILL");
      error ? reject(error) : resolve(result);
    };
    const timer = setTimeout(
      () => finish(new Error("Database query exceeded its time limit")),
      timeoutMs,
    );
    child.on("error", (error) => finish(error));
    child.on("exit", (code) =>
      finish(new Error(`Database inspection process exited (${code})`)),
    );
    child.on("message", (message) => {
      if (message.error) finish(new Error(message.error));
      else finish(null, message.result);
    });
    child.send({ modulePath, databasePath, operation, args }, (error) => {
      if (error) finish(error);
    });
  });
}
function installDatabase(sdk) {
  const registered = Object.entries(sdk.options.databases || {}).map(
    ([name, file]) => {
      if (!name || typeof file !== "string" || !path.isAbsolute(file))
        throw new Error(
          "databases must map names to absolute app database paths",
        );
      return { name, file };
    },
  );
  if (!registered.length) return;
  const resolve = async (requested) => {
    const entry = registered.find(
      (e) => e.name === requested || e.file === requested,
    );
    if (!entry) throw new Error("Database path is not registered by the app");
    const info = await fs.lstat(entry.file);
    if (!info.isFile() || info.isSymbolicLink())
      throw new Error("Database must be a regular file, not a symlink");
    const resolved = await fs.realpath(entry.file);
    const handle = await fs.open(resolved, "r");
    try {
      const header = Buffer.alloc(16);
      await handle.read(header, 0, 16, 0);
      if (header.toString() !== "SQLite format 3\0")
        throw new Error("Registered file is not a SQLite database");
    } finally {
      await handle.close();
    }
    return { ...entry, resolved, info };
  };
  const add = (id, properties, required, handler) =>
    sdk.registerTool(
      {
        id,
        category: "data",
        policy: "read",
        description:
          "Read-only SQLite inspection of app-registered databases; bounded rows, bytes, memory and execution time.",
        argumentsSchema: {
          type: "object",
          properties,
          required,
          additionalProperties: false,
        },
      },
      handler,
    );
  add(
    "data.list_databases",
    {
      includeSystemStores: { type: "boolean" },
      maxResults: { type: "integer", minimum: 1, maximum: 1000 },
    },
    [],
    async (a) => {
      const max = a.maxResults ?? 200;
      if (!Number.isInteger(max) || max < 1 || max > 1000)
        throw new Error("Invalid maxResults");
      const databases = [];
      for (const e of registered) {
        try {
          const r = await resolve(e.name);
          databases.push({
            name: path.basename(e.file),
            path: e.file,
            relativePath: e.name,
            rootAlias: "registered",
            sizeBytes: r.info.size,
            lastModifiedUtc: r.info.mtime.toISOString(),
          });
        } catch (error) {
          if (error.code !== "ENOENT") throw error;
        }
      }
      return {
        databases: databases.slice(0, max),
        truncated: databases.length > max,
        capturedAtUtc: new Date().toISOString(),
      };
    },
  );
  for (const operation of ["describe_schema", "query"]) {
    const properties = {
      path: { type: "string" },
      database: { type: "string" },
    };
    if (operation === "query")
      Object.assign(properties, {
        sql: { type: "string" },
        maxRows: { type: "integer", minimum: 1, maximum: 1000 },
      });
    else properties.table = { type: "string" };
    add(
      `data.${operation}`,
      properties,
      operation === "query" ? ["path", "sql"] : ["path"],
      async (a) => {
        const entry = await resolve(a.path ?? a.database);
        if (
          operation === "query" &&
          (typeof a.sql !== "string" || Buffer.byteLength(a.sql) > 65536)
        )
          throw new Error("SQL must be at most 65536 bytes");
        const maxRows = a.maxRows ?? 200;
        if (!Number.isInteger(maxRows) || maxRows < 1 || maxRows > 1000)
          throw new Error("Invalid maxRows");
        if (
          a.table !== undefined &&
          (typeof a.table !== "string" || a.table.length > 1024)
        )
          throw new Error("Invalid table name");
        const modulePath =
          sdk.options.sqliteModulePath ||
          require.resolve("better-sqlite3", {
            paths: [sdk.electron.app.getAppPath()],
          });
        return executeDatabase(modulePath, entry.resolved, operation, {
          ...a,
          maxRows,
        });
      },
    );
  }
}
module.exports = { installDatabase, executeDatabase };
