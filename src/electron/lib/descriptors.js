"use strict";
const fs = require("node:fs/promises"),
  { execFile } = require("node:child_process"),
  { promisify } = require("node:util");
const exec = promisify(execFile);
const kinds = {
  REG: "regular_file",
  DIR: "directory",
  IPv4: "socket",
  IPv6: "socket",
  unix: "socket",
  PIPE: "pipe",
  FIFO: "pipe",
  CHR: "character_device",
  BLK: "block_device",
  LINK: "symbolic_link",
};
const safeNumber = (s) => {
  const n = Number(s);
  return Number.isSafeInteger(n) && n >= 0 ? n : null;
};
function parseLsof(output) {
  const records = [];
  let current;
  for (const field of output.split("\0")) {
    const text = field.replace(/^\n/, ""),
      key = text[0],
      value = text.slice(1);
    if (key === "f") {
      current = /^\d+$/.test(value)
        ? {
            descriptor: Number(value),
            kind: "unknown",
            target: null,
            accessMode: null,
            closeOnExec: null,
            descriptorFlags: null,
            statusFlags: null,
            positionBytes: null,
            inode: null,
          }
        : null;
      if (current) records.push(current);
    } else if (current) {
      if (key === "t") current.kind = kinds[value] || "other";
      if (key === "n") current.target = value;
      if (key === "a")
        current.accessMode =
          { r: "read_only", w: "write_only", u: "read_write" }[value] ||
          "unknown";
      if (key === "i") current.inode = safeNumber(value);
      if (key === "o" && value.startsWith("0t"))
        current.positionBytes = safeNumber(value.slice(2));
    }
  }
  for (const r of records)
    if (!["regular_file", "directory", "block_device"].includes(r.kind))
      r.positionBytes = null;
  return records.sort((a, b) => a.descriptor - b.descriptor);
}
async function limits() {
  if (process.platform === "linux") {
    const line = (await fs.readFile("/proc/self/limits", "utf8"))
      .split("\n")
      .find((line) => line.startsWith("Max open files"));
    const match = line?.match(/Max open files\s+(\S+)\s+(\S+)/);
    if (!match) throw new Error("Descriptor limits unavailable");
    return {
      softLimit: match[1] === "unlimited" ? null : safeNumber(match[1]),
      hardLimit: match[2] === "unlimited" ? null : safeNumber(match[2]),
      hardLimitUnlimited: match[2] === "unlimited",
    };
  }
  // A non-login child inherits this process's limits; no shell startup files or app input.
  const { stdout } = await exec("/bin/sh", ["-c", "ulimit -Sn; ulimit -Hn"], {
    timeout: 3000,
    maxBuffer: 4096,
  });
  const [soft, hard] = stdout.trim().split(/\s+/);
  return {
    softLimit: soft === "unlimited" ? null : safeNumber(soft),
    hardLimit: hard === "unlimited" ? null : safeNumber(hard),
    hardLimitUnlimited: hard === "unlimited",
  };
}
async function collect() {
  if (process.platform === "darwin")
    return parseLsof(
      (
        await exec(
          "/usr/sbin/lsof",
          ["-nP", "-a", "-p", String(process.pid), "-F0fatnio", "-o"],
          { timeout: 3000, maxBuffer: 8 * 1024 * 1024 },
        )
      ).stdout,
    );
  if (process.platform !== "linux")
    throw new Error(
      "Native descriptor enumeration is unavailable on this platform",
    );
  const records = [];
  for (const name of await fs.readdir("/proc/self/fd")) {
    if (!/^\d+$/.test(name)) continue;
    try {
      const [target, info, stat] = await Promise.all([
        fs.readlink(`/proc/self/fd/${name}`),
        fs.readFile(`/proc/self/fdinfo/${name}`, "utf8"),
        fs.stat(`/proc/self/fd/${name}`),
      ]);
      const flags = parseInt(info.match(/^flags:\s+(\S+)/m)?.[1] || "0", 8);
      const kind = stat.isFile()
        ? "regular_file"
        : stat.isDirectory()
          ? "directory"
          : stat.isSocket()
            ? "socket"
            : stat.isFIFO()
              ? "pipe"
              : stat.isCharacterDevice()
                ? "character_device"
                : stat.isBlockDevice()
                  ? "block_device"
                  : target.startsWith("anon_inode:")
                    ? "anonymous_inode"
                    : "other";
      records.push({
        descriptor: Number(name),
        kind,
        target,
        accessMode:
          ["read_only", "write_only", "read_write"][flags & 3] || "unknown",
        closeOnExec: !!(flags & 0o2000000),
        descriptorFlags: null,
        statusFlags: flags,
        positionBytes: ["regular_file", "directory", "block_device"].includes(
          kind,
        )
          ? safeNumber(info.match(/^pos:\s+(\S+)/m)?.[1])
          : null,
        inode: Number.isSafeInteger(stat.ino) ? stat.ino : null,
      });
    } catch (e) {
      if (!["ENOENT", "EBADF"].includes(e.code)) throw e;
    }
  }
  return records.sort((a, b) => a.descriptor - b.descriptor);
}
function installDescriptors(sdk) {
  sdk.registerTool(
    {
      id: "electron.get_active_resources",
      policy: "read",
      category: "electron",
      description:
        "Node event-loop resource types in the Electron main process; this is not an OS descriptor count.",
    },
    () => ({
      resources: process.getActiveResourcesInfo(),
      capturedAtUtc: new Date().toISOString(),
    }),
  );
  if (!["darwin", "linux"].includes(process.platform)) return;
  for (const action of ["list_open", "count_open", "inspect", "get_usage"]) {
    const properties =
      action === "inspect"
        ? { descriptor: { type: "integer", minimum: 0 } }
        : action === "list_open"
          ? {
              kind: { type: "string" },
              targetContains: { type: "string" },
              maxEntries: { type: "integer", minimum: 1, maximum: 1000 },
            }
          : {};
    sdk.registerTool(
      {
        id: `file_descriptors.${action}`,
        category: "file_descriptors",
        policy: "read",
        description:
          "Inspect OS file descriptors belonging only to the Electron main process. Snapshots may include transient diagnostic pipes; unavailable flags are null.",
        argumentsSchema: {
          type: "object",
          properties,
          required: action === "inspect" ? ["descriptor"] : [],
          additionalProperties: false,
        },
      },
      async (a) => {
        if (
          action === "inspect" &&
          (!Number.isInteger(a.descriptor) || a.descriptor < 0)
        )
          throw new Error("Invalid descriptor");
        const max = a.maxEntries ?? 200;
        if (!Number.isInteger(max) || max < 1 || max > 1000)
          throw new Error("maxEntries must be between 1 and 1000");
        if (
          a.targetContains !== undefined &&
          (typeof a.targetContains !== "string" ||
            a.targetContains.length > 4096)
        )
          throw new Error("Invalid target filter");
        const descriptors = await collect();
        if (action === "count_open") return { count: descriptors.length };
        if (action === "inspect")
          return {
            descriptor:
              descriptors.find((d) => d.descriptor === a.descriptor) ?? null,
            capturedAtUtc: new Date().toISOString(),
          };
        const limit = await limits(),
          metadata = {
            scanComplete: true,
            scannedDescriptorLimit:
              limit.softLimit ??
              Math.max(0, ...descriptors.map((d) => d.descriptor + 1)),
            capturedAtUtc: new Date().toISOString(),
          };
        if (action === "get_usage")
          return {
            ...metadata,
            ...limit,
            openCount: descriptors.length,
            availableBeforeSoftLimit:
              limit.softLimit === null
                ? null
                : Math.max(0, limit.softLimit - descriptors.length),
            utilizationPercent: limit.softLimit
              ? (100 * descriptors.length) / limit.softLimit
              : null,
          };
        const matched = descriptors.filter(
          (d) =>
            (!a.kind || d.kind === a.kind) &&
            (!a.targetContains ||
              d.target?.toLowerCase().includes(a.targetContains.toLowerCase())),
        );
        return {
          ...metadata,
          count: descriptors.length,
          matchedCount: matched.length,
          returnedCount: Math.min(matched.length, max),
          descriptors: matched.slice(0, max),
          truncated: matched.length > max,
        };
      },
    );
  }
}
module.exports = { parseLsof, collect, limits, installDescriptors };
