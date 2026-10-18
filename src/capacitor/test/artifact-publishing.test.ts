import { describe, expect, it, vi } from "vitest";
const native = vi.hoisted(() => ({
  publishArtifact: vi.fn(async () => ({
    success: false,
    message: "host refused",
  })),
}));
vi.mock("@capacitor/core", () => ({
  Capacitor: { getPlatform: () => "ios" },
  registerPlugin: () => native,
}));
import { publishArtifact } from "../src/index";

describe("artifact publication", () => {
  for (const payload of [
    "glTF",
    { text: "glTF" },
    { base64: "Z2xURg==" },
    new Uint8Array([103, 108, 84, 70]),
  ]) {
    it("passes metadata and bytes without a tool request and preserves host failure", async () => {
      const result = await publishArtifact({
        metadata: {
          providerId: "scene",
          artifactId: "model",
          name: "Model",
          kind: "model",
          mimeType: "model/gltf-binary",
          fileName: "model.glb",
        },
        payload,
      });
      expect(result.success).toBe(false);
      expect(native.publishArtifact).toHaveBeenLastCalledWith({
        metadataJson: expect.stringContaining('"providerId":"scene"'),
        base64Data: "Z2xURg==",
      });
    });
  }
});
