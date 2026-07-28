import { Capacitor, registerPlugin } from "@capacitor/core";

import { installDomTools as installDomToolsCore } from "./dom";
import { AnsightOptionsBuilder, createOptionsBuilder } from "./options";
import type {
  AnsightArtifactDefinition,
  AnsightArtifactPayload,
  AnsightArtifactProvider,
  AnsightArtifactProviderRegistration,
  AnsightCapacitorPlugin,
  AnsightChannel,
  AnsightConnectOptions,
  AnsightDebugSnapshot,
  AnsightDomToolsOptions,
  AnsightErrorCaptureOptions,
  AnsightHostConnectionCapabilities,
  AnsightHostConnectionResult,
  AnsightHostConnectionStatus,
  AnsightLogEntry,
  AnsightOperationResult,
  AnsightOptions,
  AnsightQrPairingOptions,
  AnsightRecordedEvent,
  AnsightRecordedMetric,
  AnsightRouteTrackerOptions,
  AnsightSessionJpegCaptureOptions,
  AnsightSubscription,
  AnsightToolCall,
  AnsightToolDefinition,
  AnsightToolHandler,
  AnsightToolRegistration,
  AnsightToolResult,
} from "./definitions";

export * from "./definitions";
export { AnsightOptionsBuilder, createOptionsBuilder };

export const AnsightNative = registerPlugin<AnsightCapacitorPlugin>("Ansight");

const toolHandlers = new Map<string, AnsightToolHandler>();
const artifactProviders = new Map<string, AnsightArtifactProvider>();
const hostConnectionListeners = new Set<
  (
    status: AnsightHostConnectionStatus,
    capabilities: AnsightHostConnectionCapabilities,
  ) => void
>();
const logListeners = new Set<(entry: AnsightLogEntry) => void>();

let toolListener: Promise<AnsightSubscription> | undefined;
let logListener: Promise<AnsightSubscription> | undefined;
let lifecycleCleanup: (() => void) | undefined;
let artifactToolRegistrations: AnsightToolRegistration[] = [];
let domToolRegistration: ReturnType<typeof installDomToolsCore> | undefined;

function normalizePairingPayload(
  payload?: string | object | null,
): string | null | undefined {
  if (payload == null) return payload;
  return typeof payload === "string" ? payload : JSON.stringify(payload);
}

function normalizeOptions(input: AnsightOptions): AnsightOptions {
  const options = JSON.parse(JSON.stringify(input)) as AnsightOptions;
  if (options.pairingConfig != null) {
    options.pairingConfigJson =
      normalizePairingPayload(options.pairingConfig) ?? undefined;
    delete options.pairingConfig;
  }
  if (options.bundledPairingConfig != null) {
    options.pairingConfigJson =
      normalizePairingPayload(options.bundledPairingConfig) ?? undefined;
    delete options.bundledPairingConfig;
  }
  delete options.domTools;
  delete options.errorCapture;
  delete options.lifecycle;
  return options;
}

function normalizeToolResult(value: unknown): AnsightToolResult {
  if (value && typeof value === "object" && "success" in value) {
    return value as AnsightToolResult;
  }
  return { success: true, result: value };
}

function errorToolResult(error: unknown): AnsightToolResult {
  return {
    success: false,
    message: error instanceof Error ? error.message : String(error),
    errorCode: "javascript_tool_failed",
  };
}

async function ensureToolListener(): Promise<AnsightSubscription> {
  toolListener ??= AnsightNative.addListener(
    "ansightToolCall",
    (call: AnsightToolCall) => {
      const handler = toolHandlers.get(call.toolId);
      void Promise.resolve(
        handler
          ? handler(call.arguments ?? {}, call)
          : {
              success: false,
              message: `No JavaScript handler is registered for '${call.toolId}'.`,
              errorCode: "javascript_tool_not_registered",
            },
      )
        .then(normalizeToolResult)
        .catch(errorToolResult)
        .then((result) =>
          AnsightNative.resolveToolCall({ requestId: call.requestId, result }),
        )
        .catch(() => undefined);
    },
  );
  return toolListener;
}

async function ensureLogListener(): Promise<AnsightSubscription> {
  logListener ??= AnsightNative.addListener(
    "ansightLog",
    (entry: AnsightLogEntry) => {
      for (const listener of logListeners) listener(entry);
    },
  );
  return logListener;
}

async function emitHostConnectionStatus(): Promise<void> {
  if (hostConnectionListeners.size === 0) return;
  const [status, capabilities] = await Promise.all([
    AnsightNative.hostConnectionStatus(),
    AnsightNative.hostConnectionCapabilities(),
  ]);
  for (const listener of hostConnectionListeners)
    listener(status, capabilities);
}

async function afterConnectionChange<T>(
  operation: () => Promise<T>,
): Promise<T> {
  const result = await operation();
  await emitHostConnectionStatus();
  return result;
}

export async function initialize(
  options: AnsightOptions = {},
): Promise<AnsightDebugSnapshot> {
  const result = await AnsightNative.initialize(normalizeOptions(options));
  if (options.lifecycle !== false) startLifecycleTracking();
  if (options.errorCapture) {
    installErrorHandlers(
      typeof options.errorCapture === "object" ? options.errorCapture : {},
    );
  }
  if (options.domTools) {
    installDomTools(
      typeof options.domTools === "object" ? options.domTools : {},
    );
  }
  await emitHostConnectionStatus();
  return result;
}

export async function initializeAndActivate(
  options: AnsightOptions = {},
): Promise<AnsightDebugSnapshot> {
  const result = await AnsightNative.initializeAndActivate(
    normalizeOptions(options),
  );
  if (options.lifecycle !== false) startLifecycleTracking();
  if (options.errorCapture) {
    installErrorHandlers(
      typeof options.errorCapture === "object" ? options.errorCapture : {},
    );
  }
  if (options.domTools) {
    installDomTools(
      typeof options.domTools === "object" ? options.domTools : {},
    );
  }
  await emitHostConnectionStatus();
  return result;
}

export const activate = (): Promise<AnsightDebugSnapshot> =>
  afterConnectionChange(() => AnsightNative.activate());

export async function deactivate(): Promise<AnsightDebugSnapshot> {
  stopLifecycleTracking();
  return afterConnectionChange(() => AnsightNative.deactivate());
}

export const clear = (): Promise<AnsightDebugSnapshot> =>
  afterConnectionChange(() => AnsightNative.clear());

export const registerMetricChannel = (
  channel: AnsightChannel,
): Promise<AnsightDebugSnapshot> =>
  AnsightNative.registerMetricChannel({ channel });

export const metric = (
  value: number,
  channel = 255,
): Promise<AnsightDebugSnapshot> =>
  AnsightNative.recordMetric({ value, channel });

export const recordMetric = metric;

export function event(
  input:
    | string
    | { label: string; type?: string; details?: string; channel?: number },
): Promise<AnsightDebugSnapshot> {
  return AnsightNative.recordEvent(
    typeof input === "string" ? { label: input } : input,
  );
}

export const recordEvent = event;

export const screenViewed = (
  name: string,
  details: Record<string, string> = {},
): Promise<AnsightDebugSnapshot> =>
  AnsightNative.screenViewed({ name, details });

export const trackRoute = screenViewed;

export const setAppLifecycleState = (
  state: "unknown" | "foreground" | "background",
): Promise<AnsightDebugSnapshot> =>
  AnsightNative.setAppLifecycleState({ state });

export function connect(
  pairingPayload?: string | object | null,
  options: AnsightConnectOptions = {},
): Promise<AnsightHostConnectionResult> {
  return afterConnectionChange(() =>
    AnsightNative.connect({
      ...options,
      pairingPayload: normalizePairingPayload(pairingPayload),
    }),
  );
}

export function scanPairingQrCode(
  options: AnsightQrPairingOptions = {},
): Promise<AnsightHostConnectionResult> {
  return afterConnectionChange(() => AnsightNative.scanPairingQrCode(options));
}

export function openSession(
  pairingPayload: string | object,
  options: AnsightConnectOptions = {},
): Promise<AnsightHostConnectionResult> {
  return afterConnectionChange(() =>
    AnsightNative.openSession({
      ...options,
      pairingPayload: normalizePairingPayload(pairingPayload) ?? "",
    }),
  );
}

export const disconnect = (): Promise<AnsightHostConnectionResult> =>
  afterConnectionChange(() => AnsightNative.disconnect());

export const completeSession = (): Promise<AnsightOperationResult> =>
  afterConnectionChange(() => AnsightNative.completeSession());

export const closeSession = (): Promise<AnsightOperationResult> =>
  afterConnectionChange(() => AnsightNative.closeSession());

export const savePairingConfig = (
  pairingPayload: string | object,
  options: { expectedAppId?: string } = {},
): Promise<AnsightHostConnectionResult> =>
  afterConnectionChange(() =>
    AnsightNative.savePairingConfig({
      ...options,
      pairingPayload: normalizePairingPayload(pairingPayload) ?? "",
    }),
  );

export const clearSavedPairing = (): Promise<AnsightHostConnectionResult> =>
  afterConnectionChange(() => AnsightNative.clearSavedPairing());

export const clearSavedPairingConfig = clearSavedPairing;

export const clearCachedSession = (): Promise<AnsightOperationResult> =>
  afterConnectionChange(() => AnsightNative.clearCachedSession());

export const notifyHostConnectionConfigChanged =
  (): Promise<AnsightHostConnectionResult> =>
    afterConnectionChange(() =>
      AnsightNative.notifyHostConnectionConfigChanged(),
    );

export const status = (): Promise<AnsightDebugSnapshot> =>
  AnsightNative.status();
export const snapshot = (): Promise<AnsightDebugSnapshot> =>
  AnsightNative.snapshot();
export const hostConnectionStatus = (): Promise<AnsightHostConnectionStatus> =>
  AnsightNative.hostConnectionStatus();
export const hostConnectionCapabilities =
  (): Promise<AnsightHostConnectionCapabilities> =>
    AnsightNative.hostConnectionCapabilities();
export const currentOptions = (): Promise<
  AnsightOptions & Record<string, unknown>
> => AnsightNative.currentOptions();

export async function recordedMetrics(
  limit = 0,
): Promise<AnsightRecordedMetric[]> {
  return (await AnsightNative.recordedMetrics({ limit })).items;
}

export async function recordedEvents(
  limit = 0,
): Promise<AnsightRecordedEvent[]> {
  return (await AnsightNative.recordedEvents({ limit })).items;
}

export const sendClientLog = (line: string): Promise<AnsightOperationResult> =>
  AnsightNative.sendClientLog({ line });
export const captureBuiltInTelemetrySample =
  (): Promise<AnsightDebugSnapshot> =>
    AnsightNative.captureBuiltInTelemetrySample();
export async function isFramesPerSecondEnabled(): Promise<boolean> {
  return (await AnsightNative.isFramesPerSecondEnabled()).value;
}
export const enableFramesPerSecond = (): Promise<AnsightDebugSnapshot> =>
  AnsightNative.enableFramesPerSecond();
export const disableFramesPerSecond = (): Promise<AnsightDebugSnapshot> =>
  AnsightNative.disableFramesPerSecond();
export const captureScreenFrame = (
  options: AnsightSessionJpegCaptureOptions = {},
): Promise<AnsightOperationResult> => AnsightNative.captureScreenFrame(options);
export const enableTouchCapture = (): Promise<AnsightDebugSnapshot> =>
  AnsightNative.enableTouchCapture();
export const disableTouchCapture = (): Promise<AnsightDebugSnapshot> =>
  AnsightNative.disableTouchCapture();

export const updateSessionProperties = (
  properties: Record<string, Record<string, string>>,
): Promise<AnsightOperationResult> =>
  AnsightNative.updateSessionProperties({ properties });
export const updateCustomProperties = updateSessionProperties;
export const clearSessionProperties = (): Promise<AnsightOperationResult> =>
  AnsightNative.clearSessionProperties();
export const clearCustomProperties = clearSessionProperties;
export const registerCustomProperty = (
  group: string,
  key: string,
  value: string,
): Promise<AnsightOperationResult> =>
  AnsightNative.registerCustomProperty({ group, key, value });
export const removeCustomProperty = (
  group: string,
  key: string,
): Promise<AnsightOperationResult> =>
  AnsightNative.removeCustomProperty({ group, key });

export function addHostConnectionStatusListener(
  listener: (
    status: AnsightHostConnectionStatus,
    capabilities: AnsightHostConnectionCapabilities,
  ) => void,
  options: { emitCurrent?: boolean } = {},
): AnsightSubscription {
  hostConnectionListeners.add(listener);
  if (options.emitCurrent !== false) void emitHostConnectionStatus();
  return {
    remove: () => {
      hostConnectionListeners.delete(listener);
    },
  };
}

export function addLogListener(
  listener: (entry: AnsightLogEntry) => void,
): AnsightSubscription {
  logListeners.add(listener);
  void ensureLogListener();
  return {
    remove: () => {
      logListeners.delete(listener);
    },
  };
}

export function registerTool(
  definition: AnsightToolDefinition,
  handler: AnsightToolHandler,
): AnsightToolRegistration {
  if (!definition?.id?.trim())
    throw new TypeError("registerTool requires a stable string id.");
  if (typeof handler !== "function")
    throw new TypeError("registerTool requires a handler function.");

  toolHandlers.set(definition.id, handler);
  const ready = ensureToolListener()
    .then(() => AnsightNative.registerCustomTool({ definition }))
    .catch((error) => {
      toolHandlers.delete(definition.id);
      throw error;
    });

  return {
    id: definition.id,
    ready,
    unregister: async () => {
      toolHandlers.delete(definition.id);
      return AnsightNative.unregisterCustomTool({ id: definition.id });
    },
  };
}

export function unregisterTool(id: string): Promise<AnsightOperationResult> {
  toolHandlers.delete(id);
  return AnsightNative.unregisterCustomTool({ id });
}

export const listRegisteredTools = (): string[] => [...toolHandlers.keys()];

export async function clearRegisteredTools(): Promise<AnsightOperationResult> {
  toolHandlers.clear();
  artifactProviders.clear();
  artifactToolRegistrations = [];
  domToolRegistration = undefined;
  return AnsightNative.clearRegisteredCustomTools();
}

function bytesToBase64(bytes: Uint8Array): string {
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return btoa(binary);
}

function payloadBytes(payload: AnsightArtifactPayload): Uint8Array | undefined {
  if (payload instanceof Uint8Array) return payload;
  if (payload instanceof ArrayBuffer) return new Uint8Array(payload);
  if (Array.isArray(payload)) return Uint8Array.from(payload);
  if (typeof payload === "object" && payload) {
    const candidate = payload.bytes ?? payload.data;
    if (candidate instanceof Uint8Array) return candidate;
    if (candidate instanceof ArrayBuffer) return new Uint8Array(candidate);
    if (Array.isArray(candidate)) return Uint8Array.from(candidate);
  }
  return undefined;
}

function installArtifactTools(): AnsightToolRegistration[] {
  if (artifactToolRegistrations.length > 0) return artifactToolRegistrations;

  artifactToolRegistrations = [
    registerTool(
      {
        id: "artifacts.query",
        name: "Query JavaScript artifacts",
        description:
          "Lists artifacts exposed by Capacitor JavaScript providers.",
        category: "Artifacts",
        scope: "read",
      },
      async (_args, context) => {
        const definitions: Array<
          AnsightArtifactDefinition & { providerId: string }
        > = [];
        for (const provider of artifactProviders.values()) {
          const query = provider.queryArtifacts ?? provider.query;
          const artifacts = query
            ? await query({
                toolRequestId: context.nativeRequestId ?? context.requestId,
                sessionId: context.sessionId,
                queriedAtUtc: new Date().toISOString(),
              })
            : [];
          definitions.push(
            ...artifacts.map((artifact) => ({
              ...artifact,
              providerId: provider.descriptor.id,
            })),
          );
        }
        return {
          success: true,
          message: `Found ${definitions.length} artifact(s).`,
          result: {
            providers: [...artifactProviders.values()].map(
              ({ descriptor }) => descriptor,
            ),
            artifacts: definitions,
          },
        };
      },
    ),
    registerTool(
      {
        id: "artifacts.request",
        name: "Request JavaScript artifact",
        description:
          "Creates an artifact through a Capacitor JavaScript provider.",
        category: "Artifacts",
        scope: "read",
        argumentsSchema: {
          type: "object",
          required: ["providerId", "artifactId"],
          properties: {
            providerId: { type: "string" },
            artifactId: { type: "string" },
          },
        },
      },
      async (args, context) => {
        const provider = artifactProviders.get(args.providerId);
        const create = provider && (provider.createArtifact ?? provider.create);
        if (!provider || !create) {
          return {
            success: false,
            message: `Artifact provider '${args.providerId}' is unavailable.`,
            errorCode: "artifact_provider_not_found",
          };
        }
        const result = await create({
          providerId: args.providerId,
          artifactId: args.artifactId,
          arguments: args,
          context: {
            toolRequestId: context.nativeRequestId ?? context.requestId,
            sessionId: context.sessionId,
            requestedAtUtc: new Date().toISOString(),
          },
        });
        const bytes = payloadBytes(result.payload);
        if (bytes) {
          const transfer = await AnsightNative.queueBinaryTransfer({
            requestId: context.nativeRequestId ?? context.requestId,
            base64Data: bytesToBase64(bytes),
            chunkBytes: 64 * 1024,
          });
          return {
            success: transfer.success,
            message: transfer.message,
            result: { metadata: result.metadata, transfer },
            ...(!transfer.success && {
              errorCode: "artifact_transfer_unavailable",
            }),
          };
        }
        const payload =
          typeof result.payload === "object" &&
          result.payload &&
          "text" in result.payload
            ? result.payload.text
            : result.payload;
        return {
          success: true,
          message: `Artifact '${args.artifactId}' created.`,
          result: { metadata: result.metadata, payload },
        };
      },
    ),
  ];

  return artifactToolRegistrations;
}

export function registerArtifactProvider(
  provider: AnsightArtifactProvider,
): AnsightArtifactProviderRegistration {
  const id = provider?.descriptor?.id?.trim();
  if (!id)
    throw new TypeError("registerArtifactProvider requires descriptor.id.");
  artifactProviders.set(id, provider);
  const registrations = installArtifactTools();
  return {
    id,
    ready: Promise.all(registrations.map(({ ready }) => ready)).then(() => ({
      success: true,
      message: `Artifact provider '${id}' registered.`,
    })),
    unregister: () => unregisterArtifactProvider(id),
  };
}

export const registerArtifactProviders = (
  providers: AnsightArtifactProvider[],
): AnsightArtifactProviderRegistration[] =>
  providers.map(registerArtifactProvider);

export async function unregisterArtifactProvider(
  id: string,
): Promise<AnsightOperationResult> {
  artifactProviders.delete(id);
  if (artifactProviders.size === 0) {
    const registrations = artifactToolRegistrations;
    artifactToolRegistrations = [];
    await Promise.all(registrations.map(({ unregister }) => unregister()));
  }
  return { success: true, message: `Artifact provider '${id}' unregistered.` };
}

export const listRegisteredArtifactProviders = () =>
  [...artifactProviders.values()].map(({ descriptor }) => descriptor);

export async function clearArtifactProviders(): Promise<AnsightOperationResult> {
  artifactProviders.clear();
  const registrations = artifactToolRegistrations;
  artifactToolRegistrations = [];
  await Promise.all(registrations.map(({ unregister }) => unregister()));
  return { success: true, message: "JavaScript artifact providers cleared." };
}

export function installDomTools(options: AnsightDomToolsOptions = {}) {
  domToolRegistration ??= installDomToolsCore(registerTool, options);
  return domToolRegistration;
}

export async function uninstallDomTools(): Promise<AnsightOperationResult[]> {
  const registration = domToolRegistration;
  domToolRegistration = undefined;
  return registration ? registration.unregister() : [];
}

export function startLifecycleTracking(): void {
  if (lifecycleCleanup || typeof document === "undefined") return;
  const update = () =>
    void setAppLifecycleState(
      document.visibilityState === "visible" ? "foreground" : "background",
    );
  document.addEventListener("visibilitychange", update);
  window.addEventListener("pageshow", update);
  window.addEventListener("pagehide", update);
  lifecycleCleanup = () => {
    document.removeEventListener("visibilitychange", update);
    window.removeEventListener("pageshow", update);
    window.removeEventListener("pagehide", update);
  };
  update();
}

export function stopLifecycleTracking(): void {
  lifecycleCleanup?.();
  lifecycleCleanup = undefined;
}

export const startAppStateTracking = startLifecycleTracking;
export const stopAppStateTracking = stopLifecycleTracking;

export function installErrorHandlers(
  options: AnsightErrorCaptureOptions = {},
): () => void {
  const captureErrors = options.errors ?? true;
  const captureRejections = options.unhandledRejections ?? true;
  const captureConsole = options.consoleErrors ?? false;
  const onError = (eventValue: ErrorEvent) => {
    if (!captureErrors) return;
    void event({
      label: eventValue.message || "Unhandled JavaScript error",
      type: "Exception",
      details: JSON.stringify({
        filename: eventValue.filename,
        line: eventValue.lineno,
        column: eventValue.colno,
        stack:
          eventValue.error instanceof Error
            ? eventValue.error.stack
            : undefined,
      }),
    }).catch(() => undefined);
  };
  const onRejection = (eventValue: PromiseRejectionEvent) => {
    if (!captureRejections) return;
    const reason = eventValue.reason;
    void event({
      label:
        reason instanceof Error
          ? reason.message
          : "Unhandled JavaScript rejection",
      type: "Exception",
      details: JSON.stringify({
        reason: reason instanceof Error ? reason.stack : String(reason),
      }),
    }).catch(() => undefined);
  };
  window.addEventListener("error", onError);
  window.addEventListener("unhandledrejection", onRejection);

  const originalConsoleError = console.error;
  if (captureConsole) {
    console.error = (...args: unknown[]) => {
      originalConsoleError(...args);
      void event({
        label: "console.error",
        type: "Exception",
        details: args.map(String).join(" "),
      }).catch(() => undefined);
    };
  }

  return () => {
    window.removeEventListener("error", onError);
    window.removeEventListener("unhandledrejection", onRejection);
    if (captureConsole) console.error = originalConsoleError;
  };
}

export function createRouteTracker(
  options: AnsightRouteTrackerOptions = {},
): AnsightSubscription {
  const resolveName =
    options.resolveName ?? ((url: URL) => url.pathname + url.search + url.hash);
  const record = () =>
    void screenViewed(resolveName(new URL(location.href)), options.details);
  const originalPushState = history.pushState.bind(history);
  const originalReplaceState = history.replaceState.bind(history);
  const onPopState = () => record();
  window.addEventListener("popstate", onPopState);
  window.addEventListener("hashchange", onPopState);
  if (options.observeHistory !== false) {
    history.pushState = (...args) => {
      originalPushState(...args);
      record();
    };
    history.replaceState = (...args) => {
      originalReplaceState(...args);
      record();
    };
  }
  record();
  return {
    remove: () => {
      window.removeEventListener("popstate", onPopState);
      window.removeEventListener("hashchange", onPopState);
      history.pushState = originalPushState;
      history.replaceState = originalReplaceState;
    },
  };
}

const Ansight = {
  initialize,
  initializeAndActivate,
  activate,
  deactivate,
  clear,
  registerMetricChannel,
  metric,
  recordMetric,
  event,
  recordEvent,
  screenViewed,
  trackRoute,
  setAppLifecycleState,
  connect,
  scanPairingQrCode,
  openSession,
  disconnect,
  completeSession,
  closeSession,
  savePairingConfig,
  clearSavedPairing,
  clearSavedPairingConfig,
  clearCachedSession,
  notifyHostConnectionConfigChanged,
  status,
  snapshot,
  hostConnectionStatus,
  hostConnectionCapabilities,
  addHostConnectionStatusListener,
  currentOptions,
  recordedMetrics,
  recordedEvents,
  sendClientLog,
  addLogListener,
  captureBuiltInTelemetrySample,
  isFramesPerSecondEnabled,
  enableFramesPerSecond,
  disableFramesPerSecond,
  captureScreenFrame,
  enableTouchCapture,
  disableTouchCapture,
  updateSessionProperties,
  updateCustomProperties,
  clearSessionProperties,
  registerCustomProperty,
  removeCustomProperty,
  clearCustomProperties,
  registerTool,
  unregisterTool,
  listRegisteredTools,
  clearRegisteredTools,
  registerArtifactProvider,
  registerArtifactProviders,
  unregisterArtifactProvider,
  listRegisteredArtifactProviders,
  clearArtifactProviders,
  installDomTools,
  uninstallDomTools,
  installErrorHandlers,
  createRouteTracker,
  startLifecycleTracking,
  stopLifecycleTracking,
  startAppStateTracking,
  stopAppStateTracking,
  createOptionsBuilder,
  AnsightOptionsBuilder,
  native: AnsightNative,
  platform: Capacitor.getPlatform(),
};

export default Ansight;
