"use strict";

const {
  AppState,
  NativeEventEmitter,
  NativeModules,
  Platform,
  StyleSheet,
  UIManager,
  findNodeHandle,
  processColor,
} = require("react-native");

const nativeModule = NativeModules.AnsightReactNative;

if (!nativeModule) {
  throw new Error(
    "NativeModules.AnsightReactNative is not available. Rebuild the native app after installing @ansight/react-native."
  );
}

const toolHandlers = new Map();
const artifactProviders = new Map();
let toolEventSubscription = null;
let appStateSubscription = null;
let reactToolRegistrations = [];
let artifactToolRegistrations = [];
let reactToolNavigationRef = null;
let nextReactNodeId = 1;
const reactNodeIds = new WeakMap();
let reactFiberById = new Map();
const hostConnectionStatusListeners = new Set();
let lastHostConnectionStatusKey = null;
const logListeners = new Set();
let logEventSubscription = null;
const REACT_COMPONENT_TREE_TOOL_ID = "react.get_component_tree";
const REACT_SHADOW_TREE_TOOL_ID = "react.get_shadow_tree";

function normalizePairingPayload(payload) {
  if (payload == null) {
    return null;
  }
  return typeof payload === "string" ? payload : JSON.stringify(payload);
}

function normalizeOptions(options = {}) {
  return { ...options };
}

function cloneOptions(options = {}) {
  const clone = { ...options };
  if (options.defaultMemoryChannels) {
    clone.defaultMemoryChannels = { ...options.defaultMemoryChannels };
  }
  if (options.reactNativeMemory && typeof options.reactNativeMemory === "object") {
    clone.reactNativeMemory = { ...options.reactNativeMemory };
  }
  if (options.sessionJpegCapture && typeof options.sessionJpegCapture === "object") {
    clone.sessionJpegCapture = { ...options.sessionJpegCapture };
  }
  if (options.touchCapture && typeof options.touchCapture === "object") {
    clone.touchCapture = { ...options.touchCapture };
  }
  if (options.lifecycleCapture) {
    clone.lifecycleCapture = { ...options.lifecycleCapture };
  }
  if (options.hostAutoProbe) {
    clone.hostAutoProbe = { ...options.hostAutoProbe };
  }
  if (options.hostConnection) {
    clone.hostConnection = { ...options.hostConnection };
  }
  if (options.secureStorage) {
    clone.secureStorage = {
      ...options.secureStorage,
      allowedKeys: options.secureStorage.allowedKeys ? [...options.secureStorage.allowedKeys] : undefined,
      allowedPrefixes: options.secureStorage.allowedPrefixes ? [...options.secureStorage.allowedPrefixes] : undefined,
    };
  }
  if (options.remoteTools) {
    clone.remoteTools = cloneRemoteToolsOptions(options.remoteTools);
  }
  if (options.additionalChannels) {
    clone.additionalChannels = [...options.additionalChannels];
  }
  if (options.customProperties) {
    clone.customProperties = Object.fromEntries(
      Object.entries(options.customProperties).map(([group, properties]) => [group, { ...(properties || {}) }])
    );
  }
  return clone;
}

function cloneRoots(roots) {
  return roots ? roots.map((root) => ({ ...root })) : undefined;
}

function cloneSecureStorageToolsOptions(options = {}) {
  return {
    ...options,
    allowedKeys: options.allowedKeys ? [...options.allowedKeys] : undefined,
    allowedKeyPrefixes: options.allowedKeyPrefixes ? [...options.allowedKeyPrefixes] : undefined,
    allowedPrefixes: options.allowedPrefixes ? [...options.allowedPrefixes] : undefined,
  };
}

function cloneVisualTreeToolsOptions(options) {
  if (options == null) {
    return undefined;
  }
  if (typeof options === "boolean") {
    return options;
  }
  if (typeof options === "object") {
    return { ...options };
  }
  return undefined;
}

function cloneRemoteToolsOptions(remoteTools = {}) {
  return {
    ...remoteTools,
    visualTree: cloneVisualTreeToolsOptions(remoteTools.visualTree),
    fileSystem: remoteTools.fileSystem ? {
      ...remoteTools.fileSystem,
      additionalRoots: cloneRoots(remoteTools.fileSystem.additionalRoots),
    } : undefined,
    database: remoteTools.database ? {
      ...remoteTools.database,
      additionalRoots: cloneRoots(remoteTools.database.additionalRoots),
    } : undefined,
    preferences: remoteTools.preferences ? {
      ...remoteTools.preferences,
      allowedStores: remoteTools.preferences.allowedStores ? [...remoteTools.preferences.allowedStores] : undefined,
      allowedKeys: remoteTools.preferences.allowedKeys ? [...remoteTools.preferences.allowedKeys] : undefined,
      allowedKeyPrefixes: remoteTools.preferences.allowedKeyPrefixes ? [...remoteTools.preferences.allowedKeyPrefixes] : undefined,
    } : undefined,
    reflection: remoteTools.reflection ? {
      ...remoteTools.reflection,
      allowedRootIds: remoteTools.reflection.allowedRootIds ? [...remoteTools.reflection.allowedRootIds] : undefined,
      allowedTypePrefixes: remoteTools.reflection.allowedTypePrefixes ? [...remoteTools.reflection.allowedTypePrefixes] : undefined,
    } : undefined,
    secureStorage: remoteTools.secureStorage ? cloneSecureStorageToolsOptions(remoteTools.secureStorage) : undefined,
  };
}

class AnsightOptionsBuilder {
  constructor(options = {}) {
    this._options = cloneOptions(options);
  }

  withAnsightDefaults() {
    this._options = {
      ...this._options,
      useNativeAllInOneDefaults: true,
      sampleFrequencyMilliseconds: 400,
      retentionPeriodSeconds: 120,
      enableFramesPerSecond: true,
      enableBatteryLevel: false,
      sessionJpegCapture: {
        intervalMilliseconds: 2000,
        quality: 60,
        maxWidth: 480,
      },
      touchCapture: {},
      toolGuard: "readOnly",
      hostAutoProbe: {
        ...(this._options.hostAutoProbe || {}),
        enabled: true,
      },
    };
    return this;
  }

  withNativeAllInOneDefaults() {
    return this.withAnsightDefaults();
  }

  withAnsightSdk(configure) {
    this.withAnsightDefaults().withAllToolAccess();
    if (typeof configure === "function") {
      configure(this);
    }
    return this;
  }

  withSampleFrequencyMilliseconds(sampleFrequencyMilliseconds) {
    this._options.sampleFrequencyMilliseconds = sampleFrequencyMilliseconds;
    return this;
  }

  withFramesPerSecond() {
    this._options.enableFramesPerSecond = true;
    return this;
  }

  withoutFramesPerSecond() {
    this._options.enableFramesPerSecond = false;
    return this;
  }

  withBatteryLevel() {
    this._options.enableBatteryLevel = true;
    return this;
  }

  withoutBatteryLevel() {
    this._options.enableBatteryLevel = false;
    return this;
  }

  withRetentionPeriodSeconds(retentionPeriodSeconds) {
    this._options.retentionPeriodSeconds = retentionPeriodSeconds;
    return this;
  }

  withAdditionalChannels(additionalChannels) {
    this._options.additionalChannels = [...(additionalChannels || [])];
    return this;
  }

  addAdditionalChannel(additionalChannel) {
    this._options.additionalChannels = [...(this._options.additionalChannels || []), additionalChannel];
    return this;
  }

  withDefaultMemoryChannels(defaultMemoryChannels) {
    this._options.defaultMemoryChannels = { ...(defaultMemoryChannels || {}) };
    return this;
  }

  withoutDefaultMemoryChannels(defaultMemoryChannels) {
    const current = {
      managedHeap: true,
      javaHeap: true,
      nativeHeap: true,
      residentSetSize: true,
      rss: true,
      physicalFootprint: true,
      ...(this._options.defaultMemoryChannels || {}),
    };
    Object.keys(defaultMemoryChannels || {}).forEach((key) => {
      if (defaultMemoryChannels[key]) {
        current[key] = false;
      }
    });
    this._options.defaultMemoryChannels = current;
    return this;
  }

  withReactNativeMemoryProfiling(options = {}) {
    this._options.reactNativeMemory = {
      ...(options || {}),
      enabled: true,
    };
    return this;
  }

  withoutReactNativeMemoryProfiling() {
    this._options.reactNativeMemory = false;
    return this;
  }

  withSessionJpegCapture(optionsOrIntervalMilliseconds = {}) {
    if (typeof optionsOrIntervalMilliseconds === "number") {
      this._options.sessionJpegCapture = {
        intervalMilliseconds: optionsOrIntervalMilliseconds,
        quality: arguments.length > 1 ? arguments[1] : 60,
        maxWidth: arguments.length > 2 ? arguments[2] : 480,
        captureGpuBackedSurfaces: arguments.length > 3 ? arguments[3] : true,
      };
      return this;
    }
    this._options.sessionJpegCapture = {
      intervalMilliseconds: 2000,
      quality: 60,
      maxWidth: 480,
      ...(optionsOrIntervalMilliseconds || {}),
    };
    return this;
  }

  withoutSessionJpegCapture() {
    this._options.sessionJpegCapture = false;
    return this;
  }

  withTouchCapture(touchCapture = {}) {
    this._options.touchCapture = { ...touchCapture };
    return this;
  }

  withoutTouchCapture() {
    this._options.touchCapture = false;
    return this;
  }

  withLifecycleCapture(lifecycleCapture = {}) {
    this._options.lifecycleCapture = { ...lifecycleCapture };
    return this;
  }

  withToolGuard(toolGuard) {
    this._options.toolGuard = toolGuard;
    return this;
  }

  withToolsDisabled() {
    return this.withToolGuard("disabled");
  }

  withReadOnlyToolAccess() {
    return this.withToolGuard("readOnly");
  }

  withReadWriteToolAccess() {
    return this.withToolGuard("readWrite");
  }

  withAllToolAccess() {
    return this.withToolGuard("fullAccess");
  }

  registerCustomProperty(group, key, value) {
    const normalizedGroup = String(group || "").trim();
    const normalizedKey = String(key || "").trim();
    if (!normalizedGroup || !normalizedKey) {
      return this;
    }
    const customProperties = cloneOptions({ customProperties: this._options.customProperties }).customProperties || {};
    customProperties[normalizedGroup] = {
      ...(customProperties[normalizedGroup] || {}),
      [normalizedKey]: value == null ? "" : String(value).trim(),
    };
    this._options.customProperties = customProperties;
    return this;
  }

  removeCustomProperty(group, key) {
    const normalizedGroup = String(group || "").trim();
    const normalizedKey = String(key || "").trim();
    const customProperties = cloneOptions({ customProperties: this._options.customProperties }).customProperties || {};
    if (customProperties[normalizedGroup]) {
      delete customProperties[normalizedGroup][normalizedKey];
      if (Object.keys(customProperties[normalizedGroup]).length === 0) {
        delete customProperties[normalizedGroup];
      }
    }
    this._options.customProperties = customProperties;
    return this;
  }

  clearCustomProperties() {
    this._options.customProperties = {};
    return this;
  }

  withHostAutoProbe(hostAutoProbe = {}) {
    this._options.hostAutoProbe = {
      ...hostAutoProbe,
      enabled: true,
    };
    return this;
  }

  withoutHostAutoProbe() {
    this._options.hostAutoProbe = {
      ...(this._options.hostAutoProbe || {}),
      enabled: false,
    };
    return this;
  }

  withHostConnection(hostConnection = {}) {
    this._options.hostConnection = { ...hostConnection };
    return this;
  }

  configureHostConnection(configure) {
    const hostConnection = { ...(this._options.hostConnection || {}) };
    const configured = typeof configure === "function" ? configure(hostConnection) : hostConnection;
    this._options.hostConnection = configured || hostConnection;
    return this;
  }

  withBundledHostConnection({ bundledConfigJson = undefined } = {}) {
    return this.configureHostConnection((hostConnection) => ({
      ...hostConnection,
      bundledConfigJson,
    }));
  }

  withHostConnectionDiscoveryPort(discoveryPort) {
    return this.configureHostConnection((hostConnection) => ({
      ...hostConnection,
      discoveryPort,
    }));
  }

  withCellularHostConnections(allow = true) {
    return this.configureHostConnection((hostConnection) => ({
      ...hostConnection,
      allowCellularConnections: allow,
    }));
  }

  withHostConnectionProfileRetentionSeconds(connectionProfileRetentionSeconds) {
    return this.configureHostConnection((hostConnection) => ({
      ...hostConnection,
      connectionProfileRetentionSeconds,
    }));
  }

  withSecureStorage(secureStorage = {}) {
    this._options.secureStorage = {
      ...secureStorage,
      allowedKeys: secureStorage.allowedKeys ? [...secureStorage.allowedKeys] : undefined,
      allowedPrefixes: secureStorage.allowedPrefixes ? [...secureStorage.allowedPrefixes] : undefined,
    };
    return this;
  }

  withRemoteTools(remoteTools = {}) {
    this._options.remoteTools = cloneRemoteToolsOptions(remoteTools);
    return this;
  }

  withVisualTreeTools(options = {}) {
    this._options.remoteTools = {
      ...(this._options.remoteTools || {}),
      visualTree: typeof options === "boolean" ? options : {
        ...(options || {}),
        enabled: !options || options.enabled !== false,
      },
    };
    return this;
  }

  withoutVisualTreeTools() {
    this._options.remoteTools = {
      ...(this._options.remoteTools || {}),
      visualTree: false,
    };
    return this;
  }

  withFileSystemTools(options = {}) {
    this._options.remoteTools = {
      ...(this._options.remoteTools || {}),
      fileSystem: {
        ...options,
        additionalRoots: cloneRoots(options.additionalRoots),
      },
    };
    return this;
  }

  withDatabaseTools(options = {}) {
    this._options.remoteTools = {
      ...(this._options.remoteTools || {}),
      database: {
        ...options,
        additionalRoots: cloneRoots(options.additionalRoots),
      },
    };
    return this;
  }

  withPreferencesTools(options = {}) {
    this._options.remoteTools = {
      ...(this._options.remoteTools || {}),
      preferences: cloneRemoteToolsOptions({ preferences: options }).preferences,
    };
    return this;
  }

  withReflectionTools(options = {}) {
    this._options.remoteTools = {
      ...(this._options.remoteTools || {}),
      reflection: cloneRemoteToolsOptions({ reflection: options }).reflection,
    };
    return this;
  }

  build() {
    return cloneOptions(this._options);
  }
}

function createOptionsBuilder(options = {}) {
  return new AnsightOptionsBuilder(options);
}

async function hostConnectionStatusSnapshot() {
  const [status, capabilities] = await Promise.all([
    nativeModule.hostConnectionStatus(),
    nativeModule.hostConnectionCapabilities(),
  ]);
  return { status, capabilities };
}

function hostConnectionStatusKey(snapshot) {
  return JSON.stringify(snapshot || {});
}

async function emitHostConnectionStatusChangedIfNeeded(force = false) {
  if (hostConnectionStatusListeners.size === 0) {
    return;
  }

  try {
    const snapshot = await hostConnectionStatusSnapshot();
    const key = hostConnectionStatusKey(snapshot);
    if (!force && key === lastHostConnectionStatusKey) {
      return;
    }
    lastHostConnectionStatusKey = key;
    for (const listener of Array.from(hostConnectionStatusListeners)) {
      listener(snapshot.status, snapshot.capabilities, snapshot);
    }
  } catch {
    // Status listeners should not change the result of the SDK operation that triggered them.
  }
}

function addHostConnectionStatusListener(listener, options = {}) {
  if (typeof listener !== "function") {
    throw new TypeError("addHostConnectionStatusListener requires a listener function.");
  }

  hostConnectionStatusListeners.add(listener);
  if (options.emitCurrent !== false) {
    emitHostConnectionStatusChangedIfNeeded(true);
  }

  return {
    remove() {
      hostConnectionStatusListeners.delete(listener);
    },
  };
}

async function notifyAfterHostConnectionChange(operation) {
  const result = await operation();
  await emitHostConnectionStatusChangedIfNeeded();
  return result;
}

function normalizeLifecycleState(appState) {
  if (appState === "active") {
    return "foreground";
  }
  if (appState === "background" || appState === "inactive") {
    return "background";
  }
  return "unknown";
}

function ensureToolEventSubscription() {
  if (toolEventSubscription) {
    return;
  }

  const emitter = new NativeEventEmitter(nativeModule);
  toolEventSubscription = emitter.addListener("AnsightToolCall", async (request) => {
    const handler = toolHandlers.get(request.toolId);
    if (!handler) {
      await nativeModule.resolveToolCall(request.requestId, {
        success: false,
        message: `No JavaScript handler is registered for tool '${request.toolId}'.`,
        errorCode: "javascript_tool_handler_missing",
      });
      return;
    }

    try {
      const rawResult = await handler(request.arguments || {}, request);
      const result = rawResult && typeof rawResult === "object"
        ? rawResult
        : { success: true, result: rawResult };

      await nativeModule.resolveToolCall(request.requestId, {
        success: result.success !== false,
        message: result.message,
        errorCode: result.errorCode,
        result: result.result,
      });
    } catch (error) {
      await nativeModule.resolveToolCall(request.requestId, {
        success: false,
        message: error && error.message ? error.message : String(error),
        errorCode: "javascript_tool_exception",
      });
    }
  });
}

function ensureLogEventSubscription() {
  if (logEventSubscription) {
    return;
  }

  const emitter = new NativeEventEmitter(nativeModule);
  logEventSubscription = emitter.addListener("AnsightLog", (entry) => {
    for (const listener of Array.from(logListeners)) {
      try {
        listener(entry || {});
      } catch {
        // Log listeners should not affect the SDK bridge or other listeners.
      }
    }
  });
}

function addLogListener(listener) {
  if (typeof listener !== "function") {
    throw new TypeError("addLogListener requires a listener function.");
  }

  ensureLogEventSubscription();
  logListeners.add(listener);
  return {
    remove() {
      logListeners.delete(listener);
      if (logListeners.size === 0 && logEventSubscription) {
        logEventSubscription.remove();
        logEventSubscription = null;
      }
    },
  };
}

function startAppStateTracking() {
  if (appStateSubscription) {
    return;
  }

  nativeModule.setAppLifecycleState(normalizeLifecycleState(AppState.currentState));
  appStateSubscription = AppState.addEventListener("change", (nextState) => {
    nativeModule.setAppLifecycleState(normalizeLifecycleState(nextState));
  });
}

function stopAppStateTracking() {
  if (appStateSubscription) {
    appStateSubscription.remove();
    appStateSubscription = null;
  }
}

function parseBoolean(value, defaultValue = false) {
  if (value == null || value === "") {
    return defaultValue;
  }
  if (typeof value === "boolean") {
    return value;
  }
  const normalized = String(value).trim().toLowerCase();
  if (normalized === "true" || normalized === "1" || normalized === "yes") {
    return true;
  }
  if (normalized === "false" || normalized === "0" || normalized === "no") {
    return false;
  }
  return defaultValue;
}

function parseInteger(value, defaultValue, min, max) {
  const parsed = Number.parseInt(value, 10);
  const resolved = Number.isFinite(parsed) ? parsed : defaultValue;
  return Math.min(max, Math.max(min, resolved));
}

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
  const supportedMimeTypes = content.supportedMimeTypes || definition.supportedMimeTypes || [definition.mimeType || content.defaultMimeType || "application/octet-stream"];
  const normalizedMimeTypes = supportedMimeTypes.map((value) => trimString(value)).filter(Boolean);
  const defaultMimeType = trimString(content.defaultMimeType || definition.mimeType) || normalizedMimeTypes[0] || "application/octet-stream";
  return {
    supportedMimeTypes: normalizedMimeTypes.length ? normalizedMimeTypes : [defaultMimeType],
    defaultMimeType,
    suggestedFileName: trimString(content.suggestedFileName || definition.fileName),
    supportsText: !!content.supportsText,
    supportsBinary: content.supportsBinary !== false,
    sizeKnownBeforeCreation: !!content.sizeKnownBeforeCreation,
    estimatedSizeBytes: content.estimatedSizeBytes ?? definition.estimatedSizeBytes ?? null,
  };
}

function artifactDefinitionPayload(providerId, definition) {
  const id = trimString(definition.id);
  const name = trimString(definition.name);
  const kind = trimString(definition.kind);
  const category = trimString(definition.category);
  if (!id || !name || !kind || !category) {
    throw new TypeError("Artifact definitions require non-empty id, name, kind, and category.");
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
    argumentsSchema: definition.argumentsSchema || { type: "object", additionalProperties: true },
    security: definition.security || { level: "moderate", implications: ["metadata_disclosure"] },
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
  if (typeof ArrayBuffer !== "undefined" && ArrayBuffer.isView && ArrayBuffer.isView(value)) {
    return Array.from(new Uint8Array(value.buffer, value.byteOffset, value.byteLength));
  }
  return null;
}

function base64FromBytes(bytes) {
  if (typeof Buffer !== "undefined") {
    return Buffer.from(bytes).toString("base64");
  }
  let output = "";
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
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
  const padding = normalized.endsWith("==") ? 2 : normalized.endsWith("=") ? 1 : 0;
  return Math.max(0, Math.floor((normalized.length * 3) / 4) - padding);
}

function normalizeArtifactPayload(result) {
  const payload = result && typeof result === "object" && result.payload != null ? result.payload : result;
  if (payload && typeof payload === "object") {
    if (typeof payload.base64 === "string") {
      return { base64: payload.base64, sizeBytes: payload.sizeBytes ?? base64Size(payload.base64) };
    }
    const bytes = bytesFromValue(payload.bytes || payload.data);
    if (bytes) {
      return { base64: base64FromBytes(bytes), sizeBytes: bytes.length };
    }
    if (typeof payload.text === "string") {
      const bytesFromText = utf8Bytes(payload.text);
      return { base64: base64FromBytes(bytesFromText), sizeBytes: bytesFromText.length };
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
  throw new TypeError("Artifact result must include a payload as text, base64, bytes, ArrayBuffer, or Uint8Array.");
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
  const nested = JSON.parse(encoded);
  if (!nested || typeof nested !== "object" || Array.isArray(nested)) {
    throw new TypeError("The artifact 'arguments' value must be a JSON object.");
  }
  Object.entries(nested).forEach(([key, value]) => {
    result[key] = value == null
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
  if (!artifactId || artifactId.toLowerCase() !== request.artifactId.toLowerCase()) {
    throw new TypeError("Artifact metadata artifactId must match the requested artifact id.");
  }
  if (!providerId || providerId.toLowerCase() !== request.providerId.toLowerCase()) {
    throw new TypeError("Artifact metadata providerId must match the requested provider id.");
  }
  if (!name || !kind || !mimeType || !fileName) {
    throw new TypeError("Artifact metadata requires non-empty name, kind, mimeType, and fileName.");
  }
  return {
    artifactId,
    providerId,
    name,
    kind,
    description: trimString(metadata.description),
    mimeType,
    fileName,
    sizeBytes: metadata.sizeBytes == null ? sizeBytes : Number(metadata.sizeBytes),
    createdAtUtc: trimString(metadata.createdAtUtc) || new Date().toISOString(),
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
      description: "Queries app-provided artifact providers and currently requestable artifact definitions.",
      category: "artifacts",
      scope: "read",
      keywords: ["artifact", "artifacts", "query", "catalog", "provider", "export", "snapshot"],
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
      security: {
        level: "moderate",
        summary: "Discovers app-provided artifact definitions and descriptive metadata.",
        implications: ["metadata_disclosure"],
      },
    },
    {
      id: ARTIFACT_REQUEST_TOOL_ID,
      name: "Request Artifact",
      description: "Requests an app-provided artifact snapshot and streams it to the host.",
      category: "artifacts",
      scope: "read",
      keywords: ["artifact", "artifacts", "request", "export", "snapshot", "binary", "stream"],
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
      security: {
        level: "high",
        summary: "Requests and exports an app-provided artifact snapshot.",
        implications: ["exports_app_data", "binary_transfer"],
      },
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
      if (providerFilter && descriptor.id.toLowerCase() !== providerFilter.toLowerCase()) {
        continue;
      }
      const query = typeof provider.query === "function" ? provider.query : provider.queryArtifacts;
      const definitions = query
        ? await query.call(provider, {
          toolRequestId: context.nativeRequestId || context.requestId,
          sessionId: context.sessionId,
          queriedAtUtc: capturedAtUtc,
        })
        : [];
      providers.push({ ...descriptor, error: null });
      (definitions || []).map((definition) => artifactDefinitionPayload(descriptor.id, definition))
        .filter((definition) =>
          (!categoryFilter || definition.category.toLowerCase() === categoryFilter.toLowerCase()) &&
          (!kindFilter || definition.kind.toLowerCase() === kindFilter.toLowerCase()) &&
          (!tagFilter || definition.tags.some((tag) => tag.toLowerCase() === tagFilter.toLowerCase()))
        )
        .forEach((definition) => artifacts.push(definition));
    } catch (error) {
      const fallback = descriptor || { id: "unknown", name: "Unknown", category: "app", tags: [], metadata: {} };
      providers.push({ ...fallback, error: error && error.message ? error.message : String(error) });
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
    return { success: false, message: "Artifact request must include 'providerId'.", errorCode: "artifact_request_missing_provider_id" };
  }
  if (!artifactId) {
    return { success: false, message: "Artifact request must include 'artifactId'.", errorCode: "artifact_request_missing_artifact_id" };
  }

  let provider = null;
  for (const candidate of artifactProviders.values()) {
    if (artifactProviderDescriptor(candidate).id.toLowerCase() === providerId.toLowerCase()) {
      provider = candidate;
      break;
    }
  }
  if (!provider) {
    return { success: false, message: `Artifact provider '${providerId}' is not registered.`, errorCode: "artifact_provider_not_found" };
  }

  const nativeToolRequestId = trimString(context.nativeRequestId) || trimString(args.__ansight_requestId) || trimString(context.requestId);
  const bridgeRequestId = Platform.OS === "android" ? context.requestId : nativeToolRequestId;
  if (!bridgeRequestId) {
    return { success: false, message: "Artifact requests require a live tool protocol request context.", errorCode: "artifact_request_unavailable" };
  }
  if (typeof nativeModule.queueBinaryTransfer !== "function") {
    return { success: false, message: "Artifact binary transfer is not available in this native bridge.", errorCode: "artifact_transfer_unavailable" };
  }

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
  const create = typeof provider.create === "function" ? provider.create : provider.createArtifact;
  if (typeof create !== "function") {
    return { success: false, message: `Artifact provider '${providerId}' does not implement create().`, errorCode: "artifact_request_failed" };
  }

  const result = await create.call(provider, request);
  const payload = normalizeArtifactPayload(result);
  const metadata = artifactMetadataPayload(result && result.metadata, request, payload.sizeBytes);
  const chunkBytes = parseInteger(args.chunkBytes, 64 * 1024, 1024, 512 * 1024);
  const transfer = await nativeModule.queueBinaryTransfer(bridgeRequestId, payload.base64, chunkBytes);
  if (!transfer || transfer.success === false) {
    return {
      success: false,
      message: transfer && transfer.message ? transfer.message : "Artifact binary transfer could not be queued.",
      errorCode: transfer && transfer.errorCode ? transfer.errorCode : "artifact_transfer_unavailable",
    };
  }

  return {
    success: true,
    result: {
      artifact: metadata,
      downloadId: trimString(args.downloadId) || nativeToolRequestId || bridgeRequestId,
      transferId: transfer.transferId,
      deliveryMode: transfer.deliveryMode || "websocket_binary",
      wireProtocol: transfer.wireProtocol || FILE_TRANSFER_WIRE_PROTOCOL,
      status: transfer.status || "queued",
      chunkBytes: transfer.chunkBytes || chunkBytes,
      capturedAtUtc: requestedAtUtc,
    },
  };
}

function ensureArtifactToolsRegistered() {
  if (artifactToolRegistrations.length > 0) {
    return artifactToolRegistrations;
  }
  artifactToolRegistrations = artifactToolDefinitions().map((definition) =>
    registerTool(
      definition,
      definition.id === ARTIFACT_QUERY_TOOL_ID ? handleArtifactQuery : handleArtifactRequest
    )
  );
  return artifactToolRegistrations;
}

function registerArtifactProvider(provider) {
  if (!provider || typeof provider !== "object") {
    throw new TypeError("registerArtifactProvider requires a provider object.");
  }
  const descriptor = artifactProviderDescriptor(provider);
  artifactProviders.set(descriptor.id.toLowerCase(), provider);
  const registrations = ensureArtifactToolsRegistered();
  return {
    id: descriptor.id,
    ready: Promise.all(registrations.map((registration) => registration.ready)).then(() => ({
      success: true,
      message: `Registered artifact provider ${descriptor.id}.`,
      id: descriptor.id,
    })),
    unregister() {
      artifactProviders.delete(descriptor.id.toLowerCase());
      return Promise.resolve({
        success: true,
        message: `Unregistered artifact provider ${descriptor.id}.`,
        id: descriptor.id,
      });
    },
  };
}

function registerArtifactProviders(providers) {
  return (providers || []).map(registerArtifactProvider);
}

function unregisterArtifactProvider(providerId) {
  const id = trimString(providerId);
  if (!id) {
    return Promise.resolve({ success: false, message: "Artifact provider id must not be blank." });
  }
  const removed = artifactProviders.delete(id.toLowerCase());
  return Promise.resolve({
    success: removed,
    message: removed ? `Unregistered artifact provider ${id}.` : `Artifact provider ${id} is not registered.`,
  });
}

function listRegisteredArtifactProviders() {
  return Array.from(artifactProviders.values())
    .map((provider) => artifactProviderDescriptor(provider))
    .sort((left, right) => left.id.localeCompare(right.id));
}

function clearArtifactProviders() {
  artifactProviders.clear();
  return Promise.resolve({ success: true, message: "Cleared artifact providers." });
}

function getReactDevToolsHook() {
  return typeof global !== "undefined" ? global.__REACT_DEVTOOLS_GLOBAL_HOOK__ : undefined;
}

function rendererEntries(renderers) {
  if (!renderers) {
    return [];
  }
  if (typeof renderers.forEach === "function") {
    const entries = [];
    renderers.forEach((renderer, id) => entries.push([id, renderer]));
    return entries;
  }
  return Object.keys(renderers).map((id) => [id, renderers[id]]);
}

function rootsForRenderer(hook, rendererId) {
  if (hook && typeof hook.getFiberRoots === "function") {
    return Array.from(hook.getFiberRoots(rendererId) || []);
  }
  const fiberRoots = hook && hook._fiberRoots;
  if (fiberRoots && typeof fiberRoots.get === "function") {
    return Array.from(fiberRoots.get(rendererId) || []);
  }
  if (fiberRoots && fiberRoots[rendererId]) {
    return Array.from(fiberRoots[rendererId] || []);
  }
  return [];
}

function currentFiberRoot(root) {
  if (!root) {
    return null;
  }
  return root.current || root;
}

function getReactRoots() {
  const hook = getReactDevToolsHook();
  if (!hook) {
    return { hookAvailable: false, renderers: [], roots: [] };
  }

  const renderers = rendererEntries(hook.renderers);
  const roots = [];
  renderers.forEach(([rendererId, renderer]) => {
    rootsForRenderer(hook, rendererId).forEach((root) => {
      const current = currentFiberRoot(root);
      if (current) {
        roots.push({ rendererId: String(rendererId), renderer, fiber: current });
      }
    });
  });

  return {
    hookAvailable: true,
    renderers: renderers.map(([id, renderer]) => ({
      id: String(id),
      packageName: renderer && renderer.rendererPackageName,
      version: renderer && renderer.version,
      bundleType: renderer && renderer.bundleType,
    })),
    roots,
  };
}

function reactFiberId(fiber) {
  if (!fiber || (typeof fiber !== "object" && typeof fiber !== "function")) {
    return "react:unknown";
  }
  const existing = reactNodeIds.get(fiber);
  if (existing) {
    reactFiberById.set(existing, fiber);
    return existing;
  }
  const id = `react:${nextReactNodeId++}`;
  reactNodeIds.set(fiber, id);
  reactFiberById.set(id, fiber);
  return id;
}

function reactFiberKind(tag) {
  switch (tag) {
    case 0:
      return "function";
    case 1:
      return "class";
    case 3:
      return "root";
    case 5:
      return "host";
    case 6:
      return "text";
    case 7:
      return "fragment";
    case 9:
      return "contextConsumer";
    case 10:
      return "contextProvider";
    case 11:
      return "forwardRef";
    case 14:
    case 15:
      return "memo";
    case 19:
      return "suspenseList";
    case 22:
      return "offscreen";
    default:
      return `fiber:${tag}`;
  }
}

function reactDisplayNameFromType(type) {
  if (!type) {
    return null;
  }
  if (typeof type === "string") {
    return type;
  }
  if (typeof type === "function") {
    return type.displayName || type.name || "Anonymous";
  }
  if (typeof type === "object") {
    if (type.displayName) {
      return type.displayName;
    }
    if (type.name) {
      return type.name;
    }
    if (type.render) {
      return `ForwardRef(${reactDisplayNameFromType(type.render) || "Anonymous"})`;
    }
    if (type.type) {
      return `Memo(${reactDisplayNameFromType(type.type) || "Anonymous"})`;
    }
    if (type._context && type._context.displayName) {
      return `${type._context.displayName}.Context`;
    }
  }
  return null;
}

function reactFiberTypeName(fiber) {
  if (!fiber) {
    return "Unknown";
  }
  if (fiber.tag === 3) {
    return "ReactRoot";
  }
  if (fiber.tag === 6) {
    return "Text";
  }
  return reactDisplayNameFromType(fiber.elementType) ||
    reactDisplayNameFromType(fiber.type) ||
    reactFiberKind(fiber.tag);
}

function isSensitiveKey(key) {
  return /password|passwd|token|secret|authorization|cookie|credential|api[-_]?key|session/i.test(String(key || ""));
}

function sanitizeString(value, maxLength) {
  if (value.length <= maxLength) {
    return value;
  }
  return `${value.slice(0, Math.max(0, maxLength - 3))}...`;
}

function summarizeReactChildren(children) {
  if (children == null || typeof children === "boolean") {
    return null;
  }
  if (typeof children === "string" || typeof children === "number") {
    return sanitizeString(String(children), 160);
  }
  if (Array.isArray(children)) {
    return `[${children.length} children]`;
  }
  if (typeof children === "object") {
    return "[ReactNode]";
  }
  return typeof children;
}

function sanitizeValue(value, options, depth = 0, key = "") {
  if (isSensitiveKey(key)) {
    return "[redacted]";
  }
  if (value == null || typeof value === "boolean" || typeof value === "number") {
    return value;
  }
  if (typeof value === "string") {
    return sanitizeString(value, options.maxStringLength);
  }
  if (typeof value === "function") {
    return `[Function${value.name ? ` ${value.name}` : ""}]`;
  }
  if (typeof value === "symbol") {
    return value.toString();
  }
  if (depth >= options.maxValueDepth) {
    if (Array.isArray(value)) {
      return `[Array(${value.length})]`;
    }
    return `[${value.constructor && value.constructor.name ? value.constructor.name : "Object"}]`;
  }
  if (Array.isArray(value)) {
    return value.slice(0, options.maxArrayLength).map((entry) => sanitizeValue(entry, options, depth + 1));
  }
  if (typeof value === "object") {
    if (value.$$typeof) {
      return "[ReactElement]";
    }
    const result = {};
    Object.keys(value).slice(0, options.maxObjectKeys).forEach((entryKey) => {
      result[entryKey] = entryKey === "children"
        ? summarizeReactChildren(value[entryKey])
        : sanitizeValue(value[entryKey], options, depth + 1, entryKey);
    });
    return result;
  }
  return String(value);
}

function summarizeProps(props) {
  if (!props || typeof props !== "object") {
    return {};
  }
  const keys = Object.keys(props).filter((key) => key !== "children");
  const summary = { keys };
  ["testID", "nativeID", "accessibilityLabel", "role"].forEach((key) => {
    if (props[key] != null && !isSensitiveKey(key)) {
      summary[key] = String(props[key]);
    }
  });
  const children = summarizeReactChildren(props.children);
  if (children != null) {
    summary.children = children;
  }
  return summary;
}

function reactColorToArgbHex(value) {
  if (value == null) {
    return undefined;
  }
  try {
    const processed = typeof processColor === "function" ? processColor(value) : null;
    if (typeof processed === "number") {
      return `#${(processed >>> 0).toString(16).padStart(8, "0")}`.toUpperCase();
    }
  } catch (_) {
    // Some dynamic platform colors cannot be represented as one resolved color.
  }
  return undefined;
}

function reactVisualText(fiber, props, maxStringLength) {
  if (fiber.tag === 6) {
    return sanitizeString(String(fiber.memoizedProps || fiber.pendingProps || ""), maxStringLength);
  }
  const children = summarizeReactChildren(props.children);
  const candidate = typeof children === "string" || typeof children === "number"
    ? children
    : props.placeholder ?? props.title ?? props.accessibilityLabel;
  return candidate == null ? undefined : sanitizeString(String(candidate), maxStringLength);
}

function createReactVisual(fiber, props, maxStringLength) {
  const style = StyleSheet && typeof StyleSheet.flatten === "function"
    ? StyleSheet.flatten(props && props.style)
    : props && props.style;
  const opacity = style && Number.isFinite(Number(style.opacity))
    ? Math.max(0, Math.min(1, Number(style.opacity)))
    : 1;
  const visual = { opacity };
  const foreground = reactColorToArgbHex(style && style.color);
  const background = reactColorToArgbHex(style && style.backgroundColor);
  const text = reactVisualText(fiber, props || {}, maxStringLength);
  if (foreground) visual.foreground = foreground;
  if (background) visual.background = background;
  if (text) visual.text = text;

  if (props && !props.secureTextEntry) {
    const value = props.value ?? props.defaultValue;
    if (value != null && ["string", "number", "boolean"].includes(typeof value)) {
      visual.value = sanitizeString(String(value), maxStringLength);
    }
  }
  return visual;
}

function nativeTagForFiber(fiber) {
  const stateNode = fiber && fiber.stateNode;
  if (!stateNode) {
    return null;
  }
  if (typeof stateNode === "number") {
    return stateNode;
  }
  if (stateNode._nativeTag != null) {
    return stateNode._nativeTag;
  }
  if (stateNode.canonical && stateNode.canonical.nativeTag != null) {
    return stateNode.canonical.nativeTag;
  }
  if (typeof findNodeHandle === "function") {
    try {
      return findNodeHandle(stateNode);
    } catch (_) {
      return null;
    }
  }
  return null;
}

function measureNativeTag(nativeTag, timeoutMilliseconds = 250) {
  return new Promise((resolve) => {
    if (!nativeTag || !UIManager || typeof UIManager.measureInWindow !== "function") {
      resolve(null);
      return;
    }
    let settled = false;
    const timeout = setTimeout(() => {
      if (!settled) {
        settled = true;
        resolve(null);
      }
    }, timeoutMilliseconds);
    try {
      UIManager.measureInWindow(nativeTag, (x, y, width, height) => {
        if (settled) {
          return;
        }
        settled = true;
        clearTimeout(timeout);
        resolve({
          x,
          y,
          width,
          height,
        });
      });
    } catch (_) {
      if (!settled) {
        settled = true;
        clearTimeout(timeout);
        resolve(null);
      }
    }
  });
}

function serializeFiber(fiber, context, depth) {
  if (!fiber || context.count >= context.maxNodes || context.visited.has(fiber)) {
    context.truncated = true;
    return null;
  }
  context.visited.add(fiber);
  context.count += 1;

  const props = fiber.memoizedProps || fiber.pendingProps || {};
  const type = reactFiberTypeName(fiber);
  const kind = reactFiberKind(fiber.tag);
  const node = {
    id: reactFiberId(fiber),
    type,
    kind,
    tag: fiber.tag,
    key: fiber.key == null ? null : String(fiber.key),
    depth,
    visual: createReactVisual(fiber, props, context.maxStringLength),
    children: [],
  };

  if (fiber._debugSource) {
    node.source = {
      fileName: fiber._debugSource.fileName,
      lineNumber: fiber._debugSource.lineNumber,
      columnNumber: fiber._debugSource.columnNumber,
    };
  }

  const owner = fiber._debugOwner && reactFiberTypeName(fiber._debugOwner);
  if (owner) {
    node.owner = owner;
  }

  const nativeTag = nativeTagForFiber(fiber);
  if (nativeTag != null) {
    node.nativeTag = nativeTag;
    context.nodesWithNativeTags.push(node);
  }

  if (fiber.tag === 6) {
    node.label = sanitizeString(String(fiber.memoizedProps || fiber.pendingProps || ""), context.maxStringLength);
  } else if (props && typeof props === "object") {
    const propSummary = summarizeProps(props);
    if (Object.keys(propSummary).length > 0) {
      node.propsSummary = propSummary;
      if (propSummary.testID || propSummary.nativeID) {
        node.automationId = propSummary.testID || propSummary.nativeID;
      }
      if (!node.label && (propSummary.accessibilityLabel || typeof propSummary.children === "string")) {
        node.label = propSummary.accessibilityLabel || propSummary.children;
      }
    }
    if (context.includeProps) {
      node.props = sanitizeValue(props, context);
    }
  }

  if (context.includeState && fiber.memoizedState != null) {
    node.state = sanitizeValue(fiber.memoizedState, context);
  }

  if (depth < context.maxDepth) {
    let child = fiber.child;
    while (child) {
      const childNode = serializeFiber(child, context, depth + 1);
      if (childNode) {
        node.children.push(childNode);
      }
      child = child.sibling;
    }
  } else if (fiber.child) {
    context.truncated = true;
  }

  node.childCount = node.children.length;
  return node;
}

function isShadowTreeFiber(fiber) {
  return fiber && (fiber.tag === 3 || fiber.tag === 5 || fiber.tag === 6);
}

function createShadowTreeNode(fiber, context, depth) {
  const props = fiber.memoizedProps || fiber.pendingProps || {};
  const node = {
    id: reactFiberId(fiber),
    type: reactFiberTypeName(fiber),
    kind: fiber.tag === 3 ? "root" : fiber.tag === 6 ? "text" : "host",
    tag: fiber.tag,
    key: fiber.key == null ? null : String(fiber.key),
    depth,
    visual: createReactVisual(fiber, props, context.maxStringLength),
    children: [],
  };

  const nativeTag = nativeTagForFiber(fiber);
  if (nativeTag != null) {
    node.nativeTag = nativeTag;
    context.nodesWithNativeTags.push(node);
  }

  if (fiber.tag === 6) {
    node.label = sanitizeString(String(fiber.memoizedProps || fiber.pendingProps || ""), context.maxStringLength);
  } else if (props && typeof props === "object") {
    const propSummary = summarizeProps(props);
    if (Object.keys(propSummary).length > 0) {
      node.propsSummary = propSummary;
      if (propSummary.testID || propSummary.nativeID) {
        node.automationId = propSummary.testID || propSummary.nativeID;
      }
      if (!node.label && (propSummary.accessibilityLabel || typeof propSummary.children === "string")) {
        node.label = propSummary.accessibilityLabel || propSummary.children;
      }
    }
    if (context.includeProps) {
      node.props = sanitizeValue(props, context);
    }
  }

  return node;
}

function serializeShadowFiber(fiber, context, depth) {
  if (!fiber || context.visited.has(fiber)) {
    return [];
  }
  if (context.count >= context.maxNodes) {
    context.truncated = true;
    return [];
  }
  context.visited.add(fiber);

  const materialize = isShadowTreeFiber(fiber);
  const nodeDepth = materialize ? depth : depth - 1;
  let node = null;
  if (materialize) {
    context.count += 1;
    node = createShadowTreeNode(fiber, context, nodeDepth);
  }

  const childNodes = [];
  const childDepth = materialize ? nodeDepth + 1 : depth;
  if (!materialize || nodeDepth < context.maxDepth) {
    let child = fiber.child;
    while (child) {
      const serializedChildren = serializeShadowFiber(child, context, childDepth);
      serializedChildren.forEach((childNode) => childNodes.push(childNode));
      child = child.sibling;
    }
  } else if (fiber.child) {
    context.truncated = true;
  }

  if (!node) {
    return childNodes;
  }

  node.children = childNodes;
  node.childCount = node.children.length;
  return [node];
}

async function captureReactVisualTree(rawOptions = {}) {
  reactFiberById = new Map();
  const includeBounds = rawOptions.includeBounds !== false;
  const context = {
    includeProps: !!rawOptions.includeProps,
    includeState: !!rawOptions.includeState,
    maxArrayLength: rawOptions.maxArrayLength || 12,
    maxDepth: rawOptions.maxDepth || 30,
    maxNodes: rawOptions.maxNodes || 1500,
    maxObjectKeys: rawOptions.maxObjectKeys || 40,
    maxStringLength: rawOptions.maxStringLength || 180,
    maxValueDepth: rawOptions.maxValueDepth || 2,
    nodesWithNativeTags: [],
    count: 0,
    truncated: false,
    visited: new Set(),
  };
  const roots = getReactRoots();
  const rootNodes = roots.roots.map((root, index) => {
    const node = serializeFiber(root.fiber, context, 0);
    if (node) {
      node.rendererId = root.rendererId;
      node.rootIndex = index;
    }
    return node;
  }).filter(Boolean);

  if (includeBounds && context.nodesWithNativeTags.length > 0) {
    await Promise.all(context.nodesWithNativeTags.slice(0, 300).map(async (node) => {
      const bounds = await measureNativeTag(node.nativeTag);
      if (bounds && bounds.width > 0 && bounds.height > 0) {
        node.bounds = bounds;
      }
    }));
  }

  return {
    platform: Platform.OS,
    source: "react",
    adapter: "react.fiber",
    treeKind: "component",
    capturedAtUtc: new Date().toISOString(),
    hookAvailable: roots.hookAvailable,
    renderers: roots.renderers,
    root: {
      id: "react:roots",
      type: "ReactRoots",
      kind: "container",
      children: rootNodes,
    },
    roots: rootNodes,
    nodeCount: context.count,
    truncated: context.truncated,
    unavailableReason: roots.hookAvailable ? undefined : "React DevTools global hook is not available in this runtime.",
  };
}

async function captureReactShadowTree(rawOptions = {}) {
  reactFiberById = new Map();
  const includeBounds = rawOptions.includeBounds !== false;
  const context = {
    includeProps: !!rawOptions.includeProps,
    includeState: false,
    maxArrayLength: rawOptions.maxArrayLength || 8,
    maxDepth: rawOptions.maxDepth || 30,
    maxNodes: rawOptions.maxNodes || 1500,
    maxObjectKeys: rawOptions.maxObjectKeys || 24,
    maxStringLength: rawOptions.maxStringLength || 180,
    maxValueDepth: rawOptions.maxValueDepth || 2,
    nodesWithNativeTags: [],
    count: 0,
    truncated: false,
    visited: new Set(),
  };
  const roots = getReactRoots();
  const rootNodes = roots.roots.flatMap((root, index) => {
    const nodes = serializeShadowFiber(root.fiber, context, 0);
    nodes.forEach((node) => {
      node.rendererId = root.rendererId;
      node.rootIndex = index;
    });
    return nodes;
  });

  if (includeBounds && context.nodesWithNativeTags.length > 0) {
    await Promise.all(context.nodesWithNativeTags.slice(0, 300).map(async (node) => {
      const bounds = await measureNativeTag(node.nativeTag);
      if (bounds && bounds.width > 0 && bounds.height > 0) {
        node.bounds = bounds;
      }
    }));
  }

  return {
    platform: Platform.OS,
    source: "react-native",
    adapter: "react-native.host-fiber",
    treeKind: "shadow",
    capturedAtUtc: new Date().toISOString(),
    hookAvailable: roots.hookAvailable,
    renderers: roots.renderers,
    root: {
      id: "react:shadow-roots",
      type: "ReactNativeShadowRoots",
      kind: "container",
      children: rootNodes,
    },
    roots: rootNodes,
    nodeCount: context.count,
    truncated: context.truncated,
    unavailableReason: roots.hookAvailable ? undefined : "React DevTools global hook is not available in this runtime.",
  };
}

function flattenReactTree(node, output = []) {
  if (!node) {
    return output;
  }
  output.push(node);
  (node.children || []).forEach((child) => flattenReactTree(child, output));
  return output;
}

function nodeSearchText(node) {
  return [
    node.id,
    node.type,
    node.kind,
    node.label,
    node.owner,
    node.propsSummary && node.propsSummary.testID,
    node.propsSummary && node.propsSummary.nativeID,
    node.propsSummary && node.propsSummary.accessibilityLabel,
    node.propsSummary && node.propsSummary.children,
  ].filter(Boolean).join(" ").toLowerCase();
}

function matchesReactNode(node, args) {
  const query = args.query ? String(args.query).toLowerCase() : null;
  const type = args.type ? String(args.type).toLowerCase() : null;
  const testID = args.testID ? String(args.testID).toLowerCase() : null;
  const text = args.text ? String(args.text).toLowerCase() : null;
  const searchText = nodeSearchText(node);
  return (!query || searchText.includes(query)) &&
    (!type || String(node.type || "").toLowerCase().includes(type)) &&
    (!testID || String((node.propsSummary && node.propsSummary.testID) || "").toLowerCase() === testID) &&
    (!text || searchText.includes(text));
}

function currentNavigationObject() {
  return reactToolNavigationRef && reactToolNavigationRef.current
    ? reactToolNavigationRef.current
    : reactToolNavigationRef;
}

function navigationStateSnapshot() {
  const navigation = currentNavigationObject();
  if (!navigation) {
    return {
      available: false,
      message: "No React Navigation ref was provided to installReactTools.",
    };
  }
  const state = typeof navigation.getRootState === "function"
    ? navigation.getRootState()
    : typeof navigation.getState === "function"
      ? navigation.getState()
      : null;
  const currentRoute = typeof navigation.getCurrentRoute === "function"
    ? navigation.getCurrentRoute()
    : null;
  return {
    available: true,
    currentRoute: currentRoute ? sanitizeValue(currentRoute, { maxArrayLength: 10, maxObjectKeys: 30, maxStringLength: 160, maxValueDepth: 2 }) : null,
    state: state ? sanitizeValue(state, { maxArrayLength: 30, maxObjectKeys: 80, maxStringLength: 160, maxValueDepth: 4 }) : null,
  };
}

function reactToolOptions(baseOptions, args) {
  return {
    includeBounds: parseBoolean(args.includeBounds, baseOptions.includeBounds !== false),
    includeProps: parseBoolean(args.includeProps, !!baseOptions.includeProps),
    includeState: parseBoolean(args.includeState, !!baseOptions.includeState),
    maxDepth: parseInteger(args.maxDepth, baseOptions.maxDepth || 30, 1, 120),
    maxNodes: parseInteger(args.maxNodes, baseOptions.maxNodes || 1500, 1, 10000),
  };
}

function reactToolDefinitions(enableActions) {
  const tools = [
    {
      id: REACT_COMPONENT_TREE_TOOL_ID,
      name: "Get React Component Tree",
      description: "Captures the live React Fiber component tree for the current React Native runtime.",
      category: "react",
      scope: "read",
      keywords: ["react", "react-native", "fiber", "component", "component-tree"],
      argumentsSchema: {
        type: "object",
        properties: {
          maxDepth: { type: "integer" },
          maxNodes: { type: "integer" },
          includeBounds: { type: "boolean" },
          includeProps: { type: "boolean" },
          includeState: { type: "boolean" },
        },
      },
      resultSchema: { type: "object", additionalProperties: true },
      security: {
        level: "high",
        summary: "Inspects the React component tree and optionally sanitized props/state.",
        implications: ["inspects_runtime_state", "metadata_disclosure"],
      },
    },
    {
      id: REACT_SHADOW_TREE_TOOL_ID,
      name: "Get React Shadow Tree",
      description: "Captures the committed React Native host tree with composite components flattened out.",
      category: "react",
      scope: "read",
      keywords: ["react", "react-native", "host", "shadow", "shadow-tree", "layout"],
      argumentsSchema: {
        type: "object",
        properties: {
          maxDepth: { type: "integer" },
          maxNodes: { type: "integer" },
          includeBounds: { type: "boolean" },
          includeProps: { type: "boolean" },
        },
      },
      resultSchema: { type: "object", additionalProperties: true },
      security: {
        level: "high",
        summary: "Inspects the React Native host tree and optionally sanitized host props.",
        implications: ["inspects_runtime_state", "metadata_disclosure"],
      },
    },
    {
      id: "react.find_components",
      name: "Find React Components",
      description: "Searches the live React component tree by component type, text, testID, nativeID, or accessibility label.",
      category: "react",
      scope: "read",
      keywords: ["react", "search", "component", "testid", "accessibility"],
      argumentsSchema: {
        type: "object",
        properties: {
          query: { type: "string" },
          type: { type: "string" },
          testID: { type: "string" },
          text: { type: "string" },
          maxResults: { type: "integer" },
          includeProps: { type: "boolean" },
        },
      },
      resultSchema: { type: "object", additionalProperties: true },
      security: {
        level: "high",
        summary: "Searches React runtime metadata and may reveal UI text.",
        implications: ["inspects_runtime_state", "metadata_disclosure"],
      },
    },
    {
      id: "react.get_component",
      name: "Get React Component",
      description: "Returns a single React component node from the current React visual tree.",
      category: "react",
      scope: "read",
      keywords: ["react", "component", "props", "fiber"],
      argumentsSchema: {
        type: "object",
        properties: {
          nodeId: { type: "string" },
          includeProps: { type: "boolean" },
          includeState: { type: "boolean" },
          includeBounds: { type: "boolean" },
        },
        required: ["nodeId"],
      },
      resultSchema: { type: "object", additionalProperties: true },
      security: {
        level: "high",
        summary: "Reads one React component node and optionally sanitized props/state.",
        implications: ["inspects_runtime_state", "metadata_disclosure"],
      },
    },
    {
      id: "react.get_navigation_state",
      name: "Get React Navigation State",
      description: "Returns the configured React Navigation ref state when installReactTools receives a navigationRef.",
      category: "react",
      scope: "read",
      keywords: ["react", "navigation", "route", "screen"],
      resultSchema: { type: "object", additionalProperties: true },
      security: {
        level: "medium",
        summary: "Reads navigation route metadata from a configured navigation ref.",
        implications: ["inspects_runtime_state", "metadata_disclosure"],
      },
    },
  ];

  if (enableActions) {
    tools.push({
      id: "react.invoke_component_action",
      name: "Invoke React Component Action",
      description: "Invokes an allow-listed function prop on a React component, such as onPress.",
      category: "react",
      scope: "write",
      keywords: ["react", "component", "action", "onpress"],
      argumentsSchema: {
        type: "object",
        properties: {
          nodeId: { type: "string" },
          prop: { type: "string" },
        },
        required: ["nodeId", "prop"],
      },
      resultSchema: { type: "object", additionalProperties: true },
      security: {
        level: "critical",
        summary: "Invokes app code through a React prop function.",
        implications: ["invokes_app_code", "mutates_runtime_state"],
      },
    });
  }

  return tools;
}

function installReactTools(options = {}) {
  uninstallReactTools();
  reactToolNavigationRef = options.navigationRef || null;
  const enableActions = !!options.enableActions;
  const allowedActionProps = new Set(options.allowedActionProps || ["onPress", "onClick", "onSubmitEditing"]);
  const registrations = [];

  reactToolDefinitions(enableActions).forEach((definition) => {
    const registration = registerTool(definition, async (args = {}) => {
      if (definition.id === REACT_COMPONENT_TREE_TOOL_ID) {
        const tree = await captureReactVisualTree(reactToolOptions(options, args));
        return {
          success: tree.hookAvailable && tree.nodeCount > 0,
          message: tree.hookAvailable ? "React component tree captured." : tree.unavailableReason,
          result: tree,
        };
      }

      if (definition.id === REACT_SHADOW_TREE_TOOL_ID) {
        const tree = await captureReactShadowTree(reactToolOptions(options, args));
        return {
          success: tree.hookAvailable && tree.nodeCount > 0,
          message: tree.hookAvailable ? "React shadow tree captured." : tree.unavailableReason,
          result: tree,
        };
      }

      if (definition.id === "react.find_components") {
        const tree = await captureReactVisualTree({
          ...reactToolOptions(options, args),
          includeBounds: false,
          includeProps: parseBoolean(args.includeProps, !!options.includeProps),
        });
        const maxResults = parseInteger(args.maxResults, 50, 1, 500);
        const matches = flattenReactTree(tree.root)
          .filter((node) => node.id !== "react:roots" && matchesReactNode(node, args))
          .slice(0, maxResults);
        return {
          success: tree.hookAvailable,
          message: `Found ${matches.length} React component(s).`,
          result: {
            matches,
            count: matches.length,
            truncated: matches.length === maxResults,
          },
        };
      }

      if (definition.id === "react.get_component") {
        const tree = await captureReactVisualTree(reactToolOptions(options, args));
        const node = flattenReactTree(tree.root).find((candidate) => candidate.id === args.nodeId);
        return node
          ? { success: true, message: "React component captured.", result: node }
          : { success: false, message: `React component '${args.nodeId}' was not found.`, errorCode: "react_component_not_found" };
      }

      if (definition.id === "react.get_navigation_state") {
        return {
          success: true,
          message: "React navigation state captured.",
          result: navigationStateSnapshot(),
        };
      }

      if (definition.id === "react.invoke_component_action") {
        if (!enableActions) {
          return { success: false, message: "React component actions are disabled.", errorCode: "react_actions_disabled" };
        }
        const prop = String(args.prop || "");
        if (!allowedActionProps.has(prop)) {
          return { success: false, message: `React action prop '${prop}' is not allowed.`, errorCode: "react_action_not_allowed" };
        }
        const tree = await captureReactVisualTree({ ...reactToolOptions(options, args), includeBounds: false });
        const node = flattenReactTree(tree.root).find((candidate) => candidate.id === args.nodeId);
        if (!node) {
          return { success: false, message: `React component '${args.nodeId}' was not found.`, errorCode: "react_component_not_found" };
        }
        const fiber = reactFiberById.get(args.nodeId);
        const action = fiber && fiber.memoizedProps && fiber.memoizedProps[prop];
        if (typeof action !== "function") {
          return {
            success: false,
            message: `React component '${args.nodeId}' does not expose a function prop named '${prop}'.`,
            errorCode: "react_action_not_found",
          };
        }
        await action();
        return {
          success: true,
          message: `Invoked React component action '${prop}'.`,
          result: {
            nodeId: args.nodeId,
            prop,
            type: node.type,
          },
        };
      }

      return { success: false, message: `Unsupported React tool '${definition.id}'.`, errorCode: "react_tool_unknown" };
    });
    registrations.push(registration);
  });

  reactToolRegistrations = registrations;
  return {
    ids: registrations.map((registration) => registration.id),
    ready: Promise.all(registrations.map((registration) => registration.ready)),
    unregister: uninstallReactTools,
  };
}

function uninstallReactTools() {
  const existing = reactToolRegistrations;
  reactToolRegistrations = [];
  reactToolNavigationRef = null;
  const pending = existing.map((registration) => {
    toolHandlers.delete(registration.id);
    return nativeModule.unregisterCustomTool(registration.id).catch(() => {});
  });
  return Promise.all(pending);
}

function installErrorHandlers(options = {}) {
  const previousHandler = global.ErrorUtils && global.ErrorUtils.getGlobalHandler
    ? global.ErrorUtils.getGlobalHandler()
    : undefined;

  if (global.ErrorUtils && global.ErrorUtils.setGlobalHandler) {
    global.ErrorUtils.setGlobalHandler((error, isFatal) => {
      nativeModule.recordEvent({
        label: error && error.message ? error.message : "Unhandled JavaScript error",
        type: "Exception",
        details: JSON.stringify({
          fatal: !!isFatal,
          name: error && error.name,
          message: error && error.message,
          stack: error && error.stack,
        }),
      }).catch(() => {});

      if (typeof previousHandler === "function" && options.chain !== false) {
        previousHandler(error, isFatal);
      }
    });
  }

  const previousRejectionTracking = global.__ansightUnhandledRejectionTrackingInstalled;
  if (!previousRejectionTracking && typeof global.addEventListener === "function") {
    global.__ansightUnhandledRejectionTrackingInstalled = true;
    global.addEventListener("unhandledrejection", (event) => {
      const reason = event && event.reason;
      nativeModule.recordEvent({
        label: reason && reason.message ? reason.message : "Unhandled JavaScript promise rejection",
        type: "Exception",
        details: JSON.stringify({
          name: reason && reason.name,
          message: reason && reason.message,
          stack: reason && reason.stack,
        }),
      }).catch(() => {});
    });
  }

  return () => {
    if (global.ErrorUtils && global.ErrorUtils.setGlobalHandler && previousHandler) {
      global.ErrorUtils.setGlobalHandler(previousHandler);
    }
  };
}

async function initialize(options = {}) {
  const result = await nativeModule.initialize(normalizeOptions(options));
  if (options.lifecycle !== false) {
    startAppStateTracking();
  }
  await emitHostConnectionStatusChangedIfNeeded();
  return result;
}

async function initializeAndActivate(options = {}) {
  const result = await nativeModule.initializeAndActivate(normalizeOptions(options));
  if (options.lifecycle !== false) {
    startAppStateTracking();
  }
  await emitHostConnectionStatusChangedIfNeeded();
  return result;
}

async function deactivate() {
  stopAppStateTracking();
  return notifyAfterHostConnectionChange(() => nativeModule.deactivate());
}

function registerTool(definition, handler) {
  if (!definition || typeof definition !== "object") {
    throw new TypeError("registerTool requires a tool definition object.");
  }
  if (!definition.id || typeof definition.id !== "string") {
    throw new TypeError("registerTool requires a stable string id.");
  }
  if (typeof handler !== "function") {
    throw new TypeError("registerTool requires a JavaScript handler function.");
  }

  ensureToolEventSubscription();
  toolHandlers.set(definition.id, handler);
  const registration = nativeModule.registerCustomTool(definition);
  return {
    id: definition.id,
    ready: registration,
    unregister() {
      toolHandlers.delete(definition.id);
      return nativeModule.unregisterCustomTool(definition.id);
    },
  };
}

function unregisterTool(id) {
  toolHandlers.delete(id);
  return nativeModule.unregisterCustomTool(id);
}

function connect(pairingPayload, options = {}) {
  return notifyAfterHostConnectionChange(() => nativeModule.connect(normalizePairingPayload(pairingPayload), options));
}

function scanPairingQrCode(options = {}) {
  return notifyAfterHostConnectionChange(() => nativeModule.scanPairingQrCode(options));
}

function enrollFromQrCode(options = {}) {
  return scanPairingQrCode(options);
}

function openSession(pairingPayload, options = {}) {
  return notifyAfterHostConnectionChange(() => nativeModule.openSession(normalizePairingPayload(pairingPayload), options));
}

function recordEvent(input) {
  if (typeof input === "string") {
    return nativeModule.recordEvent({ label: input });
  }
  return nativeModule.recordEvent(input || {});
}

function screenViewed(name, details) {
  return nativeModule.screenViewed(name, details || {});
}

function trackRoute(name, details) {
  return screenViewed(name, details);
}

function createReactNavigationTracker(navigationRef, options = {}) {
  let currentRouteName;
  return {
    onReady() {
      currentRouteName = navigationRef && navigationRef.getCurrentRoute
        ? navigationRef.getCurrentRoute()?.name
        : undefined;
      if (currentRouteName && options.recordInitial !== false) {
        screenViewed(currentRouteName, { source: "react-navigation" }).catch(() => {});
      }
    },
    onStateChange() {
      const nextRouteName = navigationRef && navigationRef.getCurrentRoute
        ? navigationRef.getCurrentRoute()?.name
        : undefined;
      if (nextRouteName && nextRouteName !== currentRouteName) {
        currentRouteName = nextRouteName;
        screenViewed(nextRouteName, { source: "react-navigation" }).catch(() => {});
      }
    },
  };
}

const Ansight = {
  initialize,
  initializeAndActivate,
  activate: () => notifyAfterHostConnectionChange(() => nativeModule.activate()),
  deactivate,
  clear: () => notifyAfterHostConnectionChange(() => nativeModule.clear()),
  registerMetricChannel: (channel) => nativeModule.registerMetricChannel(channel),
  metric: (value, channel = 255) => nativeModule.recordMetric(value, channel),
  recordMetric: (value, channel = 255) => nativeModule.recordMetric(value, channel),
  event: recordEvent,
  recordEvent,
  screenViewed,
  trackRoute,
  setAppLifecycleState: (state) => nativeModule.setAppLifecycleState(state),
  connect,
  enrollFromQrCode,
  scanPairingQrCode,
  openSession,
  disconnect: () => notifyAfterHostConnectionChange(() => nativeModule.disconnect()),
  completeSession: () => notifyAfterHostConnectionChange(() => nativeModule.completeSession()),
  closeSession: () => notifyAfterHostConnectionChange(() => nativeModule.closeSession()),
  savePairingConfig: (pairingPayload, options = {}) =>
    notifyAfterHostConnectionChange(() => nativeModule.savePairingConfig(normalizePairingPayload(pairingPayload), options)),
  clearSavedPairing: () => notifyAfterHostConnectionChange(() => nativeModule.clearSavedPairing()),
  clearSavedPairingConfig: () => notifyAfterHostConnectionChange(() => nativeModule.clearSavedPairing()),
  clearCachedSession: () => notifyAfterHostConnectionChange(() => nativeModule.clearCachedSession()),
  notifyHostConnectionConfigChanged: () =>
    notifyAfterHostConnectionChange(() => nativeModule.notifyHostConnectionConfigChanged()),
  status: () => nativeModule.status(),
  snapshot: () => nativeModule.snapshot(),
  hostConnectionStatus: () => nativeModule.hostConnectionStatus(),
  hostConnectionCapabilities: () => nativeModule.hostConnectionCapabilities(),
  addHostConnectionStatusListener,
  currentOptions: () => nativeModule.currentOptions(),
  recordedMetrics: (limit = 0) => nativeModule.recordedMetrics(limit || 0),
  recordedEvents: (limit = 0) => nativeModule.recordedEvents(limit || 0),
  sendClientLog: (line) => nativeModule.sendClientLog(line),
  addLogListener,
  captureBuiltInTelemetrySample: () => nativeModule.captureBuiltInTelemetrySample(),
  isFramesPerSecondEnabled: () => nativeModule.isFramesPerSecondEnabled(),
  enableFramesPerSecond: () => nativeModule.enableFramesPerSecond(),
  disableFramesPerSecond: () => nativeModule.disableFramesPerSecond(),
  captureScreenFrame: (options = {}) => nativeModule.captureScreenFrame(options || {}),
  enableTouchCapture: () => nativeModule.enableTouchCapture(),
  disableTouchCapture: () => nativeModule.disableTouchCapture(),
  updateSessionProperties: (properties) => nativeModule.updateSessionProperties(properties || {}),
  clearSessionProperties: () => nativeModule.clearSessionProperties(),
  updateCustomProperties: (properties) => nativeModule.updateSessionProperties(properties || {}),
  registerCustomProperty: (group, key, value) => nativeModule.registerCustomProperty(group, key, value),
  removeCustomProperty: (group, key) => nativeModule.removeCustomProperty(group, key),
  clearCustomProperties: () => nativeModule.clearSessionProperties(),
  registerTool,
  unregisterTool,
  registerArtifactProvider,
  registerArtifactProviders,
  unregisterArtifactProvider,
  listRegisteredArtifactProviders,
  clearArtifactProviders,
  listRegisteredTools: () => Array.from(toolHandlers.keys()),
  clearRegisteredTools: () => {
    toolHandlers.clear();
    artifactProviders.clear();
    artifactToolRegistrations = [];
    return nativeModule.clearRegisteredCustomTools();
  },
  createOptionsBuilder,
  AnsightOptionsBuilder,
  startAppStateTracking,
  stopAppStateTracking,
  installReactTools,
  uninstallReactTools,
  installErrorHandlers,
  createReactNavigationTracker,
  platform: Platform.OS,
};

module.exports = Ansight;
module.exports.default = Ansight;
module.exports.AnsightOptionsBuilder = AnsightOptionsBuilder;
module.exports.createOptionsBuilder = createOptionsBuilder;
module.exports.notifyHostConnectionConfigChanged = Ansight.notifyHostConnectionConfigChanged;
module.exports.enrollFromQrCode = Ansight.enrollFromQrCode;
module.exports.scanPairingQrCode = Ansight.scanPairingQrCode;
module.exports.addHostConnectionStatusListener = addHostConnectionStatusListener;
module.exports.addLogListener = addLogListener;
module.exports.registerArtifactProvider = registerArtifactProvider;
module.exports.registerArtifactProviders = registerArtifactProviders;
module.exports.unregisterArtifactProvider = unregisterArtifactProvider;
module.exports.listRegisteredArtifactProviders = listRegisteredArtifactProviders;
module.exports.clearArtifactProviders = clearArtifactProviders;
