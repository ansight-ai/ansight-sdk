"use strict";
const test = require("node:test"),
  assert = require("node:assert/strict");
const { installArtifacts } = require("../lib/artifacts");
function setup() {
  const handlers = new Map();
  const sdk = {
    registerTool(d, h) {
      assert.equal(d.policy, "read");
      handlers.set(d.id, h);
    },
  };
  installArtifacts(sdk);
  const context = {
    requestId: "request-1",
    sessionId: "session-1",
    transfers: [],
  };
  return {
    sdk,
    context,
    call: (id, args = {}) => handlers.get(`artifacts.${id}`)(args, context),
  };
}
const descriptor = { id: "sample", name: "Sample provider" };
const definition = {
  id: "report",
  name: "Report",
  kind: "report",
  category: "diagnostics",
  tags: ["Test"],
};
const metadata = {
  name: "Report",
  kind: "report",
  mimeType: "text/plain",
  fileName: "report.txt",
};
test("artifact catalog filters and isolates provider errors", async () => {
  const { sdk, call } = setup();
  sdk.registerArtifactProvider({ descriptor, query: () => [definition] });
  sdk.registerArtifactProvider({
    descriptor: { id: "broken", name: "Broken" },
    query: () => {
      throw new Error("not ready");
    },
  });
  const catalog = await call("query", { tag: "test" });
  assert.equal(catalog.artifactCount, 1);
  assert.equal(catalog.providers[1].error, "not ready");
  assert.equal(
    (await call("query", { providerId: "SAMPLE", kind: "other" }))
      .artifactCount,
    0,
  );
  sdk.unregisterArtifactProvider("SAMPLE");
  assert.equal(
    (await call("query", { providerId: "sample" })).providerCount,
    0,
  );
});
test("artifact requests queue exact UTF8 bytes with native metadata", async () => {
  const { sdk, call, context } = setup();
  const registration = sdk.registerArtifactProvider({
    descriptor,
    create: (request) => {
      assert.equal(request.context.sessionId, "session-1");
      assert.equal(request.arguments.count, "2");
      return { metadata, payload: { text: "Résumé ✓" } };
    },
  });
  await registration.ready;
  const result = await call("request", {
    providerId: "sample",
    artifactId: "report",
    arguments: { count: 2 },
    chunkBytes: 1,
  });
  assert.equal(result.artifact.providerId, "sample");
  assert.equal(result.artifact.sizeBytes, Buffer.byteLength("Résumé ✓"));
  assert.equal(result.wireProtocol, "ansight.file-transfer.v1");
  assert.equal(result.chunkBytes, 1024);
  assert.equal(result.transferId, context.transfers[0].transferId);
  assert.equal(context.transfers[0].bytes.toString(), "Résumé ✓");
  registration.unregister();
  await assert.rejects(
    call("request", { providerId: "sample", artifactId: "report" }),
    /not registered/,
  );
});
test("invalid artifact metadata and arguments cannot queue a transfer", async () => {
  for (const override of [
    { artifactId: "other" },
    { sizeBytes: 99 },
    { fileName: "../secret" },
  ]) {
    const { sdk, call, context } = setup();
    sdk.registerArtifactProvider({
      descriptor,
      create: () => ({
        metadata: { ...metadata, ...override },
        payload: "abc",
      }),
    });
    await assert.rejects(
      call("request", { providerId: "sample", artifactId: "report" }),
    );
    assert.equal(context.transfers.length, 0);
    await assert.rejects(
      call("request", {
        providerId: "sample",
        artifactId: "report",
        arguments: "[]",
      }),
      /JSON object/,
    );
  }
});
