"use strict";
const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs/promises");
const os = require("node:os");
const path = require("node:path");
const { createHash } = require("node:crypto");
const { installFiles } = require("../lib/files");
test("registered files support upload, chunking, integrity, binary transfer, copy, move and delete", async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "ansight-files-"));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const handlers = new Map();
  installFiles({
    options: { fileRoots: { test: root } },
    registerTool(d, h) {
      handlers.set(d.id, h);
    },
  });
  const call = (id, args = {}, context = { transfers: [] }) =>
    handlers.get("files." + id)(args, context);
  await call("push_file", {
    fileName: "sample.txt",
    directoryPath: "",
    text: "hello world",
  });
  await assert.rejects(
    call("push_file", {
      fileName: "sample.txt",
      directoryPath: "",
      text: "wrong",
    }),
  );
  const first = await call("download_file", {
    path: "sample.txt",
    maxBytes: 5,
  });
  assert.equal(first.text, "hello");
  assert.equal(first.hasMore, true);
  const rest = await call("download_file", {
    path: "sample.txt",
    offsetBytes: first.nextOffsetBytes,
    expectedVersion: first.version,
  });
  assert.equal(rest.text, " world");
  assert.equal(rest.hasMore, false);
  await assert.rejects(
    call("download_file", { path: "sample.txt", expectedVersion: "stale" }),
    /changed/,
  );
  const sum = await call("get_file_checksum", { path: "sample.txt" });
  assert.equal(
    sum.checksums[0].checksum,
    createHash("sha256").update("hello world").digest("hex"),
  );
  const context = { transfers: [] };
  const binary = await call(
    "begin_binary_download",
    { path: "sample.txt" },
    context,
  );
  assert.equal(context.transfers[0].transferId, binary.transferId);
  assert.equal(context.transfers[0].bytes.toString(), "hello world");
  await call("copy_file", {
    sourcePath: "sample.txt",
    destinationPath: "copy.txt",
  });
  await call("move_file", {
    sourcePath: "copy.txt",
    destinationPath: "moved.txt",
  });
  await assert.rejects(fs.stat(path.join(root, "copy.txt")));
  await assert.rejects(
    call("move_file", {
      sourcePath: "sample.txt",
      destinationPath: "sample.txt",
      overwrite: true,
    }),
  );
  assert.equal(
    (await call("read_file", { path: "sample.txt" })).text,
    "hello world",
  );
  const listing = await call("list_directory");
  assert.deepEqual(listing.entries.map((e) => e.name).sort(), [
    "moved.txt",
    "sample.txt",
  ]);
  await call("delete_file", { path: "moved.txt" });
  await assert.rejects(fs.stat(path.join(root, "moved.txt")));
});
test("file roots reject traversal, unknown roots, symlinks and invalid payloads", async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "ansight-file-safety-"));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const handlers = new Map();
  installFiles({
    options: { fileRoots: { test: root } },
    registerTool(d, h) {
      handlers.set(d.id, h);
    },
  });
  const call = (id, args) =>
    handlers.get("files." + id)(args, { transfers: [] });
  await fs.symlink(os.tmpdir(), path.join(root, "outside"));
  for (const file of ["../secret", "/etc/passwd", "outside/secret"])
    await assert.rejects(call("read_file", { path: file }));
  await assert.rejects(
    call("push_file", {
      directoryPath: "outside",
      fileName: "secret",
      text: "bad",
    }),
    /Symbolic/,
  );
  await assert.rejects(call("list_directory", { root: "unknown" }), /Unknown/);
  await assert.rejects(
    call("push_file", {
      directoryPath: "",
      fileName: "bad",
      contentBase64: "!!!!",
    }),
    /base64/,
  );
  await assert.rejects(
    call("push_file", { fileName: "../bad", directoryPath: "", text: "bad" }),
  );
  await assert.rejects(call("delete_file", { path: "" }), /root/);
});
test("no file tools are exposed without explicit roots", () => {
  installFiles({
    options: {},
    registerTool() {
      assert.fail("unexpected file exposure");
    },
  });
});
