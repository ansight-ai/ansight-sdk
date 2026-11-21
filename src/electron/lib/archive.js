"use strict";
// Stored ZIP entries follow OfflineCaptureArchive.swift: fixed timestamps,
// sorted paths, streaming file reads, and a stable digest across retries.
const fs = require("node:fs"),
  fsp = fs.promises,
  path = require("node:path"),
  { createHash } = require("node:crypto");
const table = Array.from({ length: 256 }, (_, n) => {
  for (let i = 0; i < 8; i++) n = n & 1 ? 0xedb88320 ^ (n >>> 1) : n >>> 1;
  return n >>> 0;
});
async function archive(directory, destination) {
  const files = [];
  async function walk(dir) {
    for (const entry of await fsp.readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (entry.isFile() && !entry.name.endsWith(".tmp")) files.push(full);
    }
  }
  await walk(directory);
  files.sort();
  if (files.length > 65535) throw new Error("Too many archive entries");
  const output = await fsp.open(destination, "wx", 0o600),
    hash = createHash("sha256");
  let offset = 0;
  const central = [];
  async function write(bytes) {
    let position = 0;
    while (position < bytes.length) {
      const { bytesWritten } = await output.write(
        bytes,
        position,
        bytes.length - position,
      );
      if (!bytesWritten) throw new Error("Short ZIP write");
      position += bytesWritten;
    }
    hash.update(bytes);
    offset += bytes.length;
  }
  try {
    for (const file of files) {
      const name = Buffer.from(
        path.relative(directory, file).split(path.sep).join("/"),
      );
      let crc = 0xffffffff,
        size = 0;
      for await (const chunk of fs.createReadStream(file)) {
        for (const byte of chunk) crc = table[(crc ^ byte) & 255] ^ (crc >>> 8);
        size += chunk.length;
      }
      crc = (crc ^ 0xffffffff) >>> 0;
      if (size > 0xffffffff || offset > 0xffffffff || name.length > 65535)
        throw new Error("Archive exceeds ZIP32 bounds");
      const start = offset,
        header = Buffer.alloc(30);
      header.writeUInt32LE(0x04034b50);
      header.writeUInt16LE(20, 4);
      header.writeUInt16LE(0x800, 6);
      header.writeUInt16LE(0x21, 12);
      header.writeUInt32LE(crc, 14);
      header.writeUInt32LE(size, 18);
      header.writeUInt32LE(size, 22);
      header.writeUInt16LE(name.length, 26);
      await write(header);
      await write(name);
      for await (const chunk of fs.createReadStream(file)) await write(chunk);
      const c = Buffer.alloc(46);
      c.writeUInt32LE(0x02014b50);
      c.writeUInt16LE(20, 4);
      c.writeUInt16LE(20, 6);
      c.writeUInt16LE(0x800, 8);
      c.writeUInt16LE(0x21, 14);
      c.writeUInt32LE(crc, 16);
      c.writeUInt32LE(size, 20);
      c.writeUInt32LE(size, 24);
      c.writeUInt16LE(name.length, 28);
      c.writeUInt32LE(start, 42);
      central.push(c, name);
    }
    const start = offset;
    for (const bytes of central) await write(bytes);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50);
    end.writeUInt16LE(files.length, 8);
    end.writeUInt16LE(files.length, 10);
    end.writeUInt32LE(offset - start, 12);
    end.writeUInt32LE(start, 16);
    await write(end);
    await output.sync();
    return { byteCount: offset, sha256: hash.digest("hex") };
  } finally {
    await output.close();
  }
}
module.exports = { archive };
