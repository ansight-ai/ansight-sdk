"use strict";
const fs = require("node:fs/promises");
const constants = require("node:fs").constants;
const path = require("node:path");
const { isUtf8 } = require("node:buffer");
const { createHash, randomUUID } = require("node:crypto");
const MAX = 16 * 1024 * 1024;
const str = { type: "string" },
  num = { type: "integer" },
  bool = { type: "boolean" };
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) {
    crc ^= byte;
    for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  return ((crc ^ 0xffffffff) >>> 0).toString(16).padStart(8, "0");
}
function mime(file) {
  return (
    {
      ".txt": "text/plain",
      ".json": "application/json",
      ".csv": "text/csv",
      ".html": "text/html",
      ".png": "image/png",
      ".jpg": "image/jpeg",
      ".jpeg": "image/jpeg",
      ".pdf": "application/pdf",
    }[path.extname(file).toLowerCase()] || "application/octet-stream"
  );
}
function installFiles(sdk) {
  const roots = sdk.options.fileRoots || {};
  if (!Object.keys(roots).length) return;
  const required = {
    read_file: ["path"],
    download_file: ["path"],
    get_file_checksum: ["path"],
    begin_binary_download: ["path"],
    push_file: ["directoryPath", "fileName"],
    copy_file: ["sourcePath", "destinationPath"],
    move_file: ["sourcePath", "destinationPath"],
    delete_file: ["path"],
  };
  const add = (id, policy, properties, handler) =>
    sdk.registerTool(
      {
        id: "files." + id,
        category: "files",
        policy,
        description: "Operate on explicitly registered Electron app files",
        argumentsSchema: {
          type: "object",
          properties,
          required: required[id] || [],
          additionalProperties: false,
        },
      },
      async (a, c) => {
        for (const key of required[id] || [])
          if (a[key] === undefined) throw Error("Missing argument: " + key);
        return handler(a, c);
      },
    );
  async function resolve(a, key = "path", missing = false) {
    const alias = a.root || Object.keys(roots)[0];
    if (!Object.hasOwn(roots, alias)) throw Error("Unknown file root");
    const root = await fs.realpath(roots[alias]);
    const rel = a[key] ?? "";
    if (
      typeof rel !== "string" ||
      path.isAbsolute(rel) ||
      rel.split(/[\\/]/).some((p) => p === "..")
    )
      throw Error("Path must stay within its registered root");
    const target = path.resolve(root, rel);
    let cursor = root;
    for (const part of path
      .relative(root, target)
      .split(path.sep)
      .filter(Boolean)) {
      cursor = path.join(cursor, part);
      try {
        if ((await fs.lstat(cursor)).isSymbolicLink())
          throw Error("Symbolic links are not allowed");
      } catch (e) {
        if (e.code !== "ENOENT" || !missing) throw e;
      }
    }
    return {
      rootAlias: alias,
      rootPath: root,
      fullPath: target,
      relativePath: path.relative(root, target),
    };
  }
  const availableRoots = () =>
    Object.entries(roots).map(([alias, p]) => ({ alias, path: p }));
  const info = async (r) => {
    const st = await fs.stat(r.fullPath);
    return {
      rootAlias: r.rootAlias,
      rootPath: r.rootPath,
      filePath: r.fullPath,
      relativePath: r.relativePath,
      availableRoots: availableRoots(),
      fileName: path.basename(r.fullPath),
      fileExtension: path.extname(r.fullPath).slice(1) || null,
      mimeType: mime(r.fullPath),
      sizeBytes: st.size,
      lastModifiedUtc: st.mtime.toISOString(),
      version: `${st.size}:${st.mtime.toISOString()}`,
      capturedAtUtc: new Date().toISOString(),
    };
  };
  const common = { root: str, path: str };
  add(
    "list_directory",
    "read",
    {
      ...common,
      includeHidden: bool,
      recursive: bool,
      maxDepth: num,
      maxEntries: num,
    },
    async (a) => {
      const r = await resolve(a),
        entries = [],
        limit = Math.min(1000, Math.max(1, a.maxEntries || 200)),
        depth = Math.min(8, Math.max(0, a.maxDepth ?? 2));
      let truncated = false;
      async function walk(dir, d) {
        for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
          if (!a.includeHidden && entry.name.startsWith(".")) continue;
          if (entries.length >= limit) {
            truncated = true;
            return;
          }
          if (entry.isSymbolicLink()) continue;
          const full = path.join(dir, entry.name),
            st = await fs.lstat(full);
          entries.push({
            name: entry.name,
            path: full,
            isHidden: entry.name.startsWith("."),
            fileExtension: entry.isDirectory()
              ? null
              : path.extname(full).slice(1) || null,
            mimeType: entry.isDirectory() ? null : mime(full),
            rootAlias: r.rootAlias,
            relativePath: path.relative(r.rootPath, full),
            kind: entry.isSymbolicLink()
              ? "symlink"
              : entry.isDirectory()
                ? "directory"
                : "file",
            sizeBytes: st.size,
            lastModifiedUtc: st.mtime.toISOString(),
          });
          if (a.recursive && entry.isDirectory() && d < depth)
            await walk(full, d + 1);
        }
      }
      await walk(r.fullPath, 0);
      return {
        ...(await info(r)),
        directoryPath: r.fullPath,
        availableRoots: Object.entries(roots).map(([alias, p]) => ({
          alias,
          path: p,
        })),
        entries,
        truncated,
      };
    },
  );
  async function read(a) {
    const r = await resolve(a),
      meta = await info(r),
      offset = Number(a.offsetBytes ?? 0),
      max = Math.min(MAX, Math.max(1, Number(a.maxBytes) || 262144));
    if (!Number.isSafeInteger(offset) || offset < 0)
      throw Error("Invalid byte offset");
    const version = meta.version;
    if (a.expectedVersion && a.expectedVersion !== version)
      throw Error("File changed during download");
    const f = await fs.open(
      r.fullPath,
      constants.O_RDONLY | constants.O_NOFOLLOW,
    );
    try {
      const b = Buffer.alloc(
        Math.min(max, Math.max(0, meta.sizeBytes - offset)),
      );
      const { bytesRead } = await f.read(b, 0, b.length, offset);
      const bytes = b.subarray(0, bytesRead),
        hasMore = offset + bytesRead < meta.sizeBytes;
      const encoding =
        a.encoding === "base64" ||
        ((!a.encoding || a.encoding === "auto") && !isUtf8(bytes))
          ? "base64"
          : "utf8";
      if (a.encoding && !["auto", "utf8", "base64"].includes(a.encoding))
        throw Error("Unsupported encoding");
      return {
        ...meta,
        offsetBytes: offset,
        requestedMaxBytes: max,
        bytesRead,
        truncated: hasMore,
        hasMore,
        nextOffsetBytes: hasMore ? offset + bytesRead : null,
        encoding,
        contentType: encoding === "base64" ? "binary" : "text",
        text: encoding === "base64" ? null : bytes.toString("utf8"),
        base64: encoding === "base64" ? bytes.toString("base64") : null,
        nextRequest: hasMore
          ? {
              toolId: "files.download_file",
              arguments: {
                root: r.rootAlias,
                path: r.relativePath,
                offsetBytes: String(offset + bytesRead),
                maxBytes: String(max),
                encoding: a.encoding || "auto",
                expectedVersion: version,
              },
            }
          : null,
      };
    } finally {
      await f.close();
    }
  }
  add("read_file", "read", { ...common, maxBytes: num, encoding: str }, read);
  add(
    "download_file",
    "read",
    {
      ...common,
      maxBytes: num,
      encoding: str,
      offsetBytes: num,
      expectedVersion: str,
    },
    read,
  );
  add(
    "get_file_checksum",
    "read",
    { ...common, algorithms: str },
    async (a) => {
      const r = await resolve(a),
        meta = await info(r);
      if (meta.sizeBytes > MAX) throw Error("Checksum limit is 16 MiB");
      const all = ["md5", "sha1", "sha256", "sha384", "sha512", "crc32"];
      const algorithms =
        a.algorithms === "all"
          ? all
          : (a.algorithms || "sha256").split(",").map((s) => s.trim());
      if (algorithms.some((s) => !all.includes(s)))
        throw Error("Unsupported checksum algorithm");
      const b = await fs.readFile(r.fullPath);
      return {
        ...meta,
        checksums: algorithms.map((algorithm) => ({
          algorithm,
          checksum:
            algorithm === "crc32"
              ? crc32(b)
              : createHash(algorithm).update(b).digest("hex"),
          encoding: "hex",
        })),
      };
    },
  );
  add(
    "begin_binary_download",
    "read",
    { ...common, chunkBytes: num, downloadId: str },
    async (a, c) => {
      const r = await resolve(a),
        meta = await info(r);
      if (meta.sizeBytes > MAX) throw Error("Download limit is 16 MiB");
      const bytes = await fs.readFile(r.fullPath),
        transferId = randomUUID().replaceAll("-", "");
      const chunkBytes = Math.max(
        1024,
        Math.min(262144, Number(a.chunkBytes) || 65536),
      );
      c.transfers.push({ transferId, bytes, chunkBytes });
      return {
        ...meta,
        downloadId: a.downloadId || c.requestId || transferId,
        deliveryMode: "websocket_binary",
        wireProtocol: "ansight.file-transfer.v1",
        chunkBytes,
        transferId,
        fileName: path.basename(r.fullPath),
        status: "queued",
        sizeBytes: bytes.length,
        mimeType: "application/octet-stream",
        sha256: createHash("sha256").update(bytes).digest("hex"),
      };
    },
  );
  add(
    "push_file",
    "write",
    {
      root: str,
      directoryPath: str,
      fileName: str,
      contentBase64: str,
      text: str,
      overwrite: bool,
      createDirectory: bool,
    },
    async (a) => {
      if (
        !a.fileName ||
        path.basename(a.fileName) !== a.fileName ||
        /[\\/]/.test(a.fileName) ||
        [".", ".."].includes(a.fileName)
      )
        throw Error("Expected a file name");
      const r = await resolve(
        { ...a, path: path.join(a.directoryPath || "", a.fileName) },
        "path",
        true,
      );
      if (
        (typeof a.contentBase64 === "string") ===
        (typeof a.text === "string")
      )
        throw Error("Supply exactly one content encoding");
      if (
        a.contentBase64 &&
        !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(
          a.contentBase64,
        )
      )
        throw Error("Invalid base64");
      const bytes = Buffer.from(
        a.contentBase64 ?? a.text,
        a.contentBase64 != null ? "base64" : "utf8",
      );
      if (bytes.length > MAX) throw Error("Upload limit is 16 MiB");
      const overwritten = await fs.stat(r.fullPath).then(
        () => true,
        () => false,
      );
      const createdDirectory = await fs.stat(path.dirname(r.fullPath)).then(
        () => false,
        () => Boolean(a.createDirectory),
      );
      if (a.createDirectory)
        await fs.mkdir(path.dirname(r.fullPath), { recursive: true });
      const f = await fs.open(
        r.fullPath,
        constants.O_WRONLY |
          constants.O_CREAT |
          constants.O_NOFOLLOW |
          (a.overwrite ? constants.O_TRUNC : constants.O_EXCL),
        0o600,
      );
      try {
        await f.writeFile(bytes);
      } finally {
        await f.close();
      }
      return {
        ...(await info(r)),
        operation: overwritten ? "overwritten" : "created",
        overwritten,
        createdDirectory,
      };
    },
  );
  for (const operation of ["copy", "move"])
    add(
      operation + "_file",
      "write",
      {
        root: str,
        sourcePath: str,
        destinationPath: str,
        destinationRoot: str,
        overwrite: bool,
        createDirectory: bool,
      },
      async (a) => {
        const source = await resolve(a, "sourcePath"),
          dest = await resolve(
            { ...a, root: a.destinationRoot || a.root },
            "destinationPath",
            true,
          );
        if (source.fullPath === dest.fullPath)
          throw Error("Source and destination must differ");
        const overwritten = await fs.stat(dest.fullPath).then(
            () => true,
            () => false,
          ),
          createdDirectory = await fs.stat(path.dirname(dest.fullPath)).then(
            () => false,
            () => Boolean(a.createDirectory),
          );
        if (a.createDirectory)
          await fs.mkdir(path.dirname(dest.fullPath), { recursive: true });
        await fs.copyFile(
          source.fullPath,
          dest.fullPath,
          a.overwrite ? 0 : constants.COPYFILE_EXCL,
        );
        if (operation === "move") await fs.unlink(source.fullPath);
        return {
          ...(await info(dest)),
          operation: operation === "copy" ? "copied" : "moved",
          sourceRootAlias: source.rootAlias,
          sourceRootPath: source.rootPath,
          sourceFilePath: source.fullPath,
          sourceRelativePath: source.relativePath,
          destinationRootAlias: dest.rootAlias,
          destinationRootPath: dest.rootPath,
          destinationFilePath: dest.fullPath,
          destinationRelativePath: dest.relativePath,
          overwritten,
          createdDirectory,
        };
      },
    );
  add("delete_file", "critical", common, async (a) => {
    const r = await resolve(a);
    if (r.fullPath === r.rootPath) throw Error("Cannot remove a root");
    const meta = await info(r);
    await fs.unlink(r.fullPath);
    return { ...meta, deleted: true };
  });
}
module.exports = { installFiles };
