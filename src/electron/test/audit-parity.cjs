"use strict";
const fs = require("node:fs"),
  path = require("node:path");
const { createAnsight } = require("..");
const sdk = createAnsight({
  appId: "audit",
  enabled: false,
  electron: {},
  fileRoots: { fixture: __dirname },
  databases: { fixture: path.join(__dirname, "fixture.sqlite") },
  secureStorage: { store: "fixture", get() {}, set() {}, remove() {} },
});
sdk.registerArtifactProvider({
  descriptor: { id: "fixture", name: "Fixture" },
  query: () => [],
});
const source = path.resolve(__dirname, "../../ios/Sources");
const families = [
    "VisualTree",
    "FileSystem",
    "Preferences",
    "SecureStorage",
    "Database",
    "Reflection",
    "FileDescriptorDiagnostics",
  ],
  rows = [];
for (const family of families) {
  const dir = path.join(source, `AnsightTools${family}`),
    file = fs.readdirSync(dir).find((name) => name.endsWith("ToolIds.swift"));
  const ids = [
    ...fs
      .readFileSync(path.join(dir, file), "utf8")
      .matchAll(/= "([a-z_]+\.[a-z_]+)"/g),
  ].map((m) => m[1]);
  for (const id of ids)
    rows.push({ family, id, implemented: sdk.tools.has(id) });
}
console.log(
  JSON.stringify(
    {
      baseline:
        "Native iOS standard tool IDs; optional Electron providers explicitly registered",
      rows,
      missing: rows.filter((row) => !row.implemented),
      electronTools: [...sdk.tools.keys()],
    },
    null,
    2,
  ),
);
