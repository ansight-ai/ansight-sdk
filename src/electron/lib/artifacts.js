"use strict";
// Artifact descriptors and provider semantics mirror the React Native SDK.
const { randomUUID } = require("node:crypto");
const path = require("node:path");
function parseInteger(value, fallback, min, max) {
  const parsed = Number.parseInt(value, 10);
  return Math.min(
    max,
    Math.max(min, Number.isFinite(parsed) ? parsed : fallback),
  );
}
function installArtifacts(sdk) {
  const artifactProviders = new Map();
  const ARTIFACT_QUERY_TOOL_ID = "artifacts.query";
  const ARTIFACT_REQUEST_TOOL_ID = "artifacts.request";
  const FILE_TRANSFER_WIRE_PROTOCOL = "ansight.file-transfer.v1";
  const ARTIFACT_REQUEST_RESERVED_KEYS = new Set([
    "providerId",
    "artifactId",
    "downloadId",
    "chunkBytes",
    "arguments",
    "__ansight_requestId",
    "__ansight_sessionId",
  ]);

  function trimString(value) {
    const text = value == null ? "" : String(value).trim();
    return text || null;
  }

  function normalizedTags(tags) {
    const seen = new Set();
    return (Array.isArray(tags) ? tags : [])
      .map((tag) => trimString(tag))
      .filter((tag) => {
        if (!tag) {
          return false;
        }
        const key = tag.toLowerCase();
        if (seen.has(key)) {
          return false;
        }
        seen.add(key);
        return true;
      });
  }

  function normalizedMetadata(metadata) {
    const result = {};
    Object.entries(metadata || {}).forEach(([key, value]) => {
      const normalizedKey = trimString(key);
      if (normalizedKey) {
        result[normalizedKey] = value == null ? "" : String(value).trim();
      }
    });
    return result;
  }

  function artifactProviderDescriptor(provider) {
    const descriptor = provider.descriptor || provider;
    const id = trimString(descriptor.id);
    const name = trimString(descriptor.name);
    if (!id) {
      throw new TypeError("Artifact provider id must not be blank.");
    }
    if (!name) {
      throw new TypeError("Artifact provider name must not be blank.");
    }
    return {
      id,
      name,
      description: trimString(descriptor.description),
      category: trimString(descriptor.category) || "app",
      tags: normalizedTags(descriptor.tags),
      metadata: normalizedMetadata(descriptor.metadata),
    };
  }

  function artifactContentDescriptor(definition) {
    const content = definition.content || {};
    const supportedMimeTypes = content.supportedMimeTypes ||
      definition.supportedMimeTypes || [
        definition.mimeType ||
          content.defaultMimeType ||
          "application/octet-stream",
      ];
    const normalizedMimeTypes = supportedMimeTypes
      .map((value) => trimString(value))
      .filter(Boolean);
    const defaultMimeType =
      trimString(content.defaultMimeType || definition.mimeType) ||
      normalizedMimeTypes[0] ||
      "application/octet-stream";
    return {
      supportedMimeTypes: normalizedMimeTypes.length
        ? normalizedMimeTypes
        : [defaultMimeType],
      defaultMimeType,
      suggestedFileName: trimString(
        content.suggestedFileName || definition.fileName,
      ),
      supportsText: !!content.supportsText,
      supportsBinary: content.supportsBinary !== false,
      sizeKnownBeforeCreation: !!content.sizeKnownBeforeCreation,
      estimatedSizeBytes:
        content.estimatedSizeBytes ?? definition.estimatedSizeBytes ?? null,
    };
  }

  function artifactDefinitionPayload(providerId, definition) {
    const id = trimString(definition.id);
    const name = trimString(definition.name);
    const kind = trimString(definition.kind);
    const category = trimString(definition.category);
    if (!id || !name || !kind || !category) {
      throw new TypeError(
        "Artifact definitions require non-empty id, name, kind, and category.",
      );
    }
    return {
      providerId,
      id,
      name,
      description: trimString(definition.description) || "",
      kind,
      category,
      tags: normalizedTags(definition.tags),
      metadata: normalizedMetadata(definition.metadata),
      content: artifactContentDescriptor(definition),
      argumentsSchema: definition.argumentsSchema || {
        type: "object",
        additionalProperties: true,
      },
    };
  }

  function utf8Bytes(text) {
    if (typeof TextEncoder !== "undefined") {
      return Array.from(new TextEncoder().encode(text));
    }
    const encoded = unescape(encodeURIComponent(text));
    const bytes = new Array(encoded.length);
    for (let index = 0; index < encoded.length; index += 1) {
      bytes[index] = encoded.charCodeAt(index);
    }
    return bytes;
  }

  function bytesFromValue(value) {
    if (value == null) {
      return null;
    }
    if (Array.isArray(value)) {
      return value.map((byte) => Number(byte) & 0xff);
    }
    if (typeof ArrayBuffer !== "undefined" && value instanceof ArrayBuffer) {
      return Array.from(new Uint8Array(value));
    }
    if (
      typeof ArrayBuffer !== "undefined" &&
      ArrayBuffer.isView &&
      ArrayBuffer.isView(value)
    ) {
      return Array.from(
        new Uint8Array(value.buffer, value.byteOffset, value.byteLength),
      );
    }
    return null;
  }

  function base64FromBytes(bytes) {
    if (typeof Buffer !== "undefined") {
      return Buffer.from(bytes).toString("base64");
    }
    let output = "";
    const alphabet =
      "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    for (let index = 0; index < bytes.length; index += 3) {
      const first = bytes[index];
      const second = index + 1 < bytes.length ? bytes[index + 1] : 0;
      const third = index + 2 < bytes.length ? bytes[index + 2] : 0;
      const triple = (first << 16) | (second << 8) | third;
      output += alphabet[(triple >> 18) & 63];
      output += alphabet[(triple >> 12) & 63];
      output += index + 1 < bytes.length ? alphabet[(triple >> 6) & 63] : "=";
      output += index + 2 < bytes.length ? alphabet[triple & 63] : "=";
    }
    return output;
  }

  function base64Size(base64) {
    const normalized = String(base64 || "").replace(/\s/g, "");
    if (!normalized) {
      return 0;
    }
    const padding = normalized.endsWith("==")
      ? 2
      : normalized.endsWith("=")
        ? 1
        : 0;
    return Math.max(0, Math.floor((normalized.length * 3) / 4) - padding);
  }

  function normalizeArtifactPayload(result) {
    const payload =
      result && typeof result === "object" && result.payload != null
        ? result.payload
        : result;
    if (payload && typeof payload === "object") {
      if (typeof payload.base64 === "string") {
        return {
          base64: payload.base64,
          sizeBytes: payload.sizeBytes ?? base64Size(payload.base64),
        };
      }
      const bytes = bytesFromValue(payload.bytes || payload.data);
      if (bytes) {
        return { base64: base64FromBytes(bytes), sizeBytes: bytes.length };
      }
      if (typeof payload.text === "string") {
        const bytesFromText = utf8Bytes(payload.text);
        return {
          base64: base64FromBytes(bytesFromText),
          sizeBytes: bytesFromText.length,
        };
      }
    }
    if (typeof payload === "string") {
      const bytes = utf8Bytes(payload);
      return { base64: base64FromBytes(bytes), sizeBytes: bytes.length };
    }
    const bytes = bytesFromValue(payload);
    if (bytes) {
      return { base64: base64FromBytes(bytes), sizeBytes: bytes.length };
    }
    throw new TypeError(
      "Artifact result must include a payload as text, base64, bytes, ArrayBuffer, or Uint8Array.",
    );
  }

  function artifactRequestArguments(args = {}) {
    const result = {};
    Object.entries(args || {}).forEach(([key, value]) => {
      if (!ARTIFACT_REQUEST_RESERVED_KEYS.has(key) && value != null) {
        result[key] = String(value);
      }
    });
    const encoded = trimString(args.arguments);
    if (!encoded) {
      return result;
    }
    const nested =
      typeof args.arguments === "object" ? args.arguments : JSON.parse(encoded);
    if (!nested || typeof nested !== "object" || Array.isArray(nested)) {
      throw new TypeError(
        "The artifact 'arguments' value must be a JSON object.",
      );
    }
    Object.entries(nested).forEach(([key, value]) => {
      result[key] =
        value == null
          ? ""
          : typeof value === "object"
            ? JSON.stringify(value)
            : String(value);
    });
    return result;
  }

  function artifactMetadataPayload(rawMetadata, request, sizeBytes) {
    const metadata = rawMetadata || {};
    const artifactId = trimString(metadata.artifactId || request.artifactId);
    const providerId = trimString(metadata.providerId || request.providerId);
    const name = trimString(metadata.name);
    const kind = trimString(metadata.kind);
    const mimeType = trimString(metadata.mimeType);
    const fileName = trimString(metadata.fileName);
    if (
      !artifactId ||
      artifactId.toLowerCase() !== request.artifactId.toLowerCase()
    ) {
      throw new TypeError(
        "Artifact metadata artifactId must match the requested artifact id.",
      );
    }
    if (
      !providerId ||
      providerId.toLowerCase() !== request.providerId.toLowerCase()
    ) {
      throw new TypeError(
        "Artifact metadata providerId must match the requested provider id.",
      );
    }
    if (!name || !kind || !mimeType || !fileName) {
      throw new TypeError(
        "Artifact metadata requires non-empty name, kind, mimeType, and fileName.",
      );
    }
    return {
      artifactId,
      providerId,
      name,
      kind,
      description: trimString(metadata.description),
      mimeType,
      fileName,
      sizeBytes:
        metadata.sizeBytes == null ? sizeBytes : Number(metadata.sizeBytes),
      createdAtUtc:
        trimString(metadata.createdAtUtc) || new Date().toISOString(),
      tags: normalizedTags(metadata.tags),
      metadata: normalizedMetadata(metadata.metadata),
    };
  }

  function artifactToolDefinitions() {
    const genericObject = { type: "object", additionalProperties: true };
    return [
      {
        id: ARTIFACT_QUERY_TOOL_ID,
        name: "Query Artifacts",
        description:
          "Queries app-provided artifact providers and currently requestable artifact definitions.",
        category: "artifacts",
        policy: "read",
        keywords: [
          "artifact",
          "artifacts",
          "query",
          "catalog",
          "provider",
          "export",
          "snapshot",
        ],
        argumentsSchema: {
          type: "object",
          properties: {
            providerId: { type: ["string", "null"] },
            category: { type: ["string", "null"] },
            kind: { type: ["string", "null"] },
            tag: { type: ["string", "null"] },
          },
        },
        resultSchema: genericObject,
      },
      {
        id: ARTIFACT_REQUEST_TOOL_ID,
        name: "Request Artifact",
        description:
          "Requests an app-provided artifact snapshot and streams it to the host.",
        category: "artifacts",
        policy: "read",
        keywords: [
          "artifact",
          "artifacts",
          "request",
          "export",
          "snapshot",
          "binary",
          "stream",
        ],
        argumentsSchema: {
          type: "object",
          properties: {
            providerId: { type: "string" },
            artifactId: { type: "string" },
            downloadId: { type: ["string", "null"] },
            chunkBytes: { type: "integer" },
            arguments: genericObject,
          },
          required: ["providerId", "artifactId"],
        },
        resultSchema: genericObject,
      },
    ];
  }

  async function handleArtifactQuery(args = {}, context = {}) {
    const capturedAtUtc = new Date().toISOString();
    const providerFilter = trimString(args.providerId);
    const categoryFilter = trimString(args.category);
    const kindFilter = trimString(args.kind);
    const tagFilter = trimString(args.tag);
    const providers = [];
    const artifacts = [];

    for (const provider of artifactProviders.values()) {
      let descriptor;
      try {
        descriptor = artifactProviderDescriptor(provider);
        if (
          providerFilter &&
          descriptor.id.toLowerCase() !== providerFilter.toLowerCase()
        ) {
          continue;
        }
        const query =
          typeof provider.query === "function"
            ? provider.query
            : provider.queryArtifacts;
        const definitions = query
          ? await query.call(provider, {
              toolRequestId: context.nativeRequestId || context.requestId,
              sessionId: context.sessionId,
              queriedAtUtc: capturedAtUtc,
            })
          : [];
        providers.push({ ...descriptor, error: null });
        (definitions || [])
          .map((definition) =>
            artifactDefinitionPayload(descriptor.id, definition),
          )
          .filter(
            (definition) =>
              (!categoryFilter ||
                definition.category.toLowerCase() ===
                  categoryFilter.toLowerCase()) &&
              (!kindFilter ||
                definition.kind.toLowerCase() === kindFilter.toLowerCase()) &&
              (!tagFilter ||
                definition.tags.some(
                  (tag) => tag.toLowerCase() === tagFilter.toLowerCase(),
                )),
          )
          .forEach((definition) => artifacts.push(definition));
      } catch (error) {
        const fallback = descriptor || {
          id: "unknown",
          name: "Unknown",
          category: "app",
          tags: [],
          metadata: {},
        };
        providers.push({
          ...fallback,
          error: error && error.message ? error.message : String(error),
        });
      }
    }

    return {
      success: true,
      result: {
        providers,
        artifacts,
        providerCount: providers.length,
        artifactCount: artifacts.length,
        capturedAtUtc,
      },
    };
  }

  async function handleArtifactRequest(args = {}, context = {}) {
    const providerId = trimString(args.providerId);
    const artifactId = trimString(args.artifactId);
    if (!providerId) {
      return {
        success: false,
        message: "Artifact request must include 'providerId'.",
        errorCode: "artifact_request_missing_provider_id",
      };
    }
    if (!artifactId) {
      return {
        success: false,
        message: "Artifact request must include 'artifactId'.",
        errorCode: "artifact_request_missing_artifact_id",
      };
    }

    let provider = null;
    for (const candidate of artifactProviders.values()) {
      if (
        artifactProviderDescriptor(candidate).id.toLowerCase() ===
        providerId.toLowerCase()
      ) {
        provider = candidate;
        break;
      }
    }
    if (!provider) {
      return {
        success: false,
        message: `Artifact provider '${providerId}' is not registered.`,
        errorCode: "artifact_provider_not_found",
      };
    }

    if (!context.requestId || !Array.isArray(context.transfers))
      throw new Error(
        "Artifact requests require a live tool protocol request context.",
      );
    const nativeToolRequestId = context.requestId;
    const bridgeRequestId = context.requestId;
    const requestedAtUtc = new Date().toISOString();
    const request = {
      providerId,
      artifactId,
      arguments: artifactRequestArguments(args),
      context: {
        toolRequestId: nativeToolRequestId || bridgeRequestId,
        sessionId: context.sessionId || args.__ansight_sessionId,
        requestedAtUtc,
      },
    };
    const create =
      typeof provider.create === "function"
        ? provider.create
        : provider.createArtifact;
    if (typeof create !== "function") {
      return {
        success: false,
        message: `Artifact provider '${providerId}' does not implement create().`,
        errorCode: "artifact_request_failed",
      };
    }

    const result = await create.call(provider, request);
    const payload = normalizeArtifactPayload(result);
    const metadata = artifactMetadataPayload(
      result && result.metadata,
      request,
      payload.sizeBytes,
    );
    const chunkBytes = parseInteger(
      args.chunkBytes,
      64 * 1024,
      1024,
      512 * 1024,
    );
    const bytes = Buffer.from(payload.base64, "base64");
    if (bytes.length > 64 * 1024 * 1024)
      throw new Error("Electron artifacts are limited to 64 MiB");
    if (metadata.sizeBytes !== bytes.length)
      throw new Error("Artifact metadata sizeBytes must match payload");
    if (path.basename(metadata.fileName) !== metadata.fileName)
      throw new Error("Artifact filename must not contain a path");
    const transfer = {
      transferId: randomUUID().replaceAll("-", ""),
      bytes,
      chunkBytes,
    };
    context.transfers.push(transfer);

    return {
      success: true,
      result: {
        artifact: metadata,
        downloadId:
          trimString(args.downloadId) || nativeToolRequestId || bridgeRequestId,
        transferId: transfer.transferId,
        deliveryMode: transfer.deliveryMode || "websocket_binary",
        wireProtocol: transfer.wireProtocol || FILE_TRANSFER_WIRE_PROTOCOL,
        status: transfer.status || "queued",
        chunkBytes: transfer.chunkBytes || chunkBytes,
        capturedAtUtc: requestedAtUtc,
      },
    };
  }

  let installed = false;
  sdk.registerArtifactProvider = (provider) => {
    if (!provider || typeof provider !== "object")
      throw new TypeError("Artifact provider must be an object");
    const descriptor = artifactProviderDescriptor(provider);
    artifactProviders.set(descriptor.id.toLowerCase(), provider);
    if (!installed) {
      for (const definition of artifactToolDefinitions()) {
        const handler =
          definition.id === ARTIFACT_QUERY_TOOL_ID
            ? handleArtifactQuery
            : handleArtifactRequest;
        sdk.registerTool(definition, async (args, context) => {
          const response = await handler(args, context);
          if (!response.success)
            throw Object.assign(new Error(response.message), {
              code: response.errorCode,
            });
          return response.result;
        });
      }
      installed = true;
    }
    return {
      id: descriptor.id,
      ready: Promise.resolve({ success: true, id: descriptor.id }),
      unregister: () => sdk.unregisterArtifactProvider(descriptor.id),
    };
  };
  sdk.registerArtifactProviders = (providers) =>
    providers.map(sdk.registerArtifactProvider);
  sdk.unregisterArtifactProvider = (providerId) => ({
    success: artifactProviders.delete(String(providerId).trim().toLowerCase()),
  });
  sdk.listRegisteredArtifactProviders = () =>
    [...artifactProviders.values()]
      .map(artifactProviderDescriptor)
      .sort((a, b) => a.id.localeCompare(b.id));
  sdk.clearArtifactProviders = () => {
    artifactProviders.clear();
    return { success: true };
  };
}
module.exports = { installArtifacts };
