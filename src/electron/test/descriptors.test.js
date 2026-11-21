"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict"),
  fs = require("node:fs/promises"),
  os = require("node:os"),
  path = require("node:path");
const {
  parseLsof,
  collect,
  limits,
  installDescriptors,
} = require("../lib/descriptors");
test("lsof parser excludes mappings and preserves native descriptor metadata", () => {
  const result = parseLsof(
    "p12\0\nfcwd\0tDIR\0n/app\0\nftxt\0tREG\0n/bin\0\nf7\0ar\0tREG\0o0t42\0i123\0n/app/a b\nfile\0\nf8\0au\0tIPv4\0o0t0\0n127.0.0.1:123\0\n",
  );
  assert.equal(result.length, 2);
  assert.equal(result[0].descriptor, 7);
  assert.equal(result[0].target, "/app/a b\nfile");
  assert.equal(result[0].positionBytes, 42);
  assert.equal(result[0].accessMode, "read_only");
  assert.equal(result[1].kind, "socket");
  assert.equal(result[1].positionBytes, null);
  assert.equal(result[0].closeOnExec, null);
});
test(
  "OS diagnostics identify a real open file and its current offset",
  { skip: !["darwin", "linux"].includes(process.platform) },
  async () => {
    const dir = await fs.mkdtemp(path.join(os.tmpdir(), "ansight-fd-")),
      file = path.join(dir, "fixture.txt");
    await fs.writeFile(file, "descriptor verification");
    const handle = await fs.open(file, "r");
    try {
      await handle.read(Buffer.alloc(3), 0, 3, null);
      const descriptors = await collect(),
        found = descriptors.find((d) => d.descriptor === handle.fd);
      assert.ok(found);
      assert.equal(found.kind, "regular_file");
      assert.equal(found.target, await fs.realpath(file));
      assert.equal(found.positionBytes, 3);
      assert.equal(found.accessMode, "read_only");
      const actualLimits = await limits();
      assert.ok(actualLimits.softLimit > descriptors.length);
      const tools = new Map();
      installDescriptors({ registerTool: (d, h) => tools.set(d.id, { d, h }) });
      const listed = await tools
        .get("file_descriptors.list_open")
        .h({ targetContains: "fixture.txt", maxEntries: 1 });
      assert.equal(listed.returnedCount, 1);
      assert.equal(listed.descriptors[0].descriptor, handle.fd);
      const inspected = await tools
        .get("file_descriptors.inspect")
        .h({ descriptor: handle.fd });
      assert.equal(inspected.descriptor.positionBytes, 3);
      const usage = await tools.get("file_descriptors.get_usage").h({});
      assert.equal(
        usage.availableBeforeSoftLimit,
        usage.softLimit - usage.openCount,
      );
    } finally {
      await handle.close();
      await fs.rm(dir, { recursive: true, force: true });
    }
  },
);
