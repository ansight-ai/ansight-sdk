import type { BrowserWindow } from "electron";
import { EventEmitter } from "node:events";
export interface AnsightElectronOptions {
  appId: string;
  appName?: string;
  enabled: boolean;
  /** @deprecated Use toolGuard. */
  allowActions?: boolean;
  toolGuard?: "disabled" | "readOnly" | "readWrite" | "fullAccess";
  /** Native-compatible cadence, clamped to 200–2000 ms; default 400. */
  sampleFrequencyMilliseconds?: number;
  /** @deprecated Use sampleFrequencyMilliseconds. */
  sampleIntervalMilliseconds?: number;
  /** Local readback window, clamped to 60–3600 seconds; default 120. */
  retentionPeriodSeconds?: number;
  additionalChannels?: Array<{
    id: number;
    name: string;
    unit?: string;
    kind?: string;
    color?: string;
    group?: string;
  }>;

  sessionJpegCapture?:
    | false
    | {
        intervalMilliseconds?: number;
        quality?: number;
        maxWidth?: number | null;
        mode?:
          | "screenshotOnly"
          | "screenshotAndVisualTree"
          | "screenshotWithVisualTreeOnTouch";
      };
  offlineCapture?: {
    enabled: boolean;
    recordingEnabled?: boolean;
    maximumSessions?: number;
    transferMode?: "automatic" | "manual";
    maximumSessionBytes?: number;
    maximumRetainedBytes?: number;
    maximumQueuedBytes?: number;
    rootDirectory?: string;
  };
  crashCapture?:
    | false
    | {
        enabled?: boolean;
        hostHandoffEnabled?: boolean;
        /** Opt-in ownership of Electron Crashpad. Local dumps only, no uploads; cannot be disabled until process exit. Do not combine with another crashReporter owner. */
        nativeSignalCapture?: boolean;
        maximumPendingReports?: number;
        retentionDays?: number;
        maximumBreadcrumbs?: number;
        maximumTraceBytes?: number;
      };
  /** Measures visible-window animation callbacks, not GPU presentation FPS. */
  enableFramesPerSecond?: boolean;
  /** false disables touch capture configuration, including runtime re-enabling. */
  touchCapture?: boolean;
  captureNetwork?: boolean;
  networkCapture?: {
    redactSensitiveData?: boolean;
    /** Synchronous sanitizer; return null to exclude a request. Runs before transport/offline persistence. */
    sanitizer?: (request: NetworkRequest) => NetworkRequest | null;
  };
  fileRoots?: Record<string, string>;
  /** Explicit app-owned SQLite files, keyed by an inspection alias. */
  databases?: Record<string, string>;
  /** Optional absolute module path; defaults to the app’s better-sqlite3 dependency. */
  sqliteModulePath?: string;
  /** Adapter to the app's actual secure store; tools are critical-policy only. */
  secureStorage?: {
    store: string;
    get(key: string): string | null | Promise<string | null>;
    set(key: string, value: string): void | Promise<void>;
    remove(key: string): boolean | Promise<boolean>;
  };
  allowPackaged?: boolean;
  discoveryPort?: number;
  captureIntervalMs?: number;
}
export interface NetworkBody {
  contentType?: string;
  encoding: "utf8" | "base64";
  data: string;
  totalBytes?: number;
  capturedBytes?: number;
  truncated?: boolean;
}
export interface NetworkRequest {
  id?: string;
  source?: string;
  startedAtUtc?: string;
  completedAtUtc?: string;
  durationMilliseconds?: number;
  method?: string;
  url: string;
  protocol?: string;
  requestHeaders?: Array<{ name: string; value: string }>;
  responseHeaders?: Array<{ name: string; value: string }>;
  requestBody?: NetworkBody;
  responseBody?: NetworkBody;
  requestBodySizeBytes?: number;
  responseBodySizeBytes?: number;
  statusCode?: number;
  reasonPhrase?: string;
  errorType?: string;
  errorMessage?: string;
  performance?: {
    timeToFirstByteMilliseconds?: number;
    dnsMilliseconds?: number;
    connectMilliseconds?: number;
    tlsMilliseconds?: number;
    uploadDurationMilliseconds?: number;
    downloadDurationMilliseconds?: number;
    uploadedBytes?: number;
    downloadedBytes?: number;
    reusedConnection?: boolean;
  };
}
export interface ToolDescriptor {
  id: string;
  name?: string;
  description?: string;
  category?: string;
  policy: "read" | "write" | "critical";
  argumentsSchema?: object;
  resultSchema?: object;
}
export interface ArtifactProvider {
  descriptor: {
    id: string;
    name: string;
    description?: string;
    category?: string;
    tags?: string[];
    metadata?: Record<string, string>;
  };
  query(context: {
    toolRequestId?: string;
    sessionId?: string;
    queriedAtUtc: string;
  }): ArtifactDefinition[] | Promise<ArtifactDefinition[]>;
  create(request: {
    providerId: string;
    artifactId: string;
    arguments: Record<string, string>;
    context: {
      toolRequestId: string;
      sessionId?: string;
      requestedAtUtc: string;
    };
  }): ArtifactResult | Promise<ArtifactResult>;
}
export interface ArtifactDefinition {
  id: string;
  name: string;
  kind: string;
  category: string;
  description?: string;
  tags?: string[];
  metadata?: Record<string, string>;
  mimeType?: string;
  fileName?: string;
  argumentsSchema?: object;
  content?: {
    supportedMimeTypes?: string[];
    defaultMimeType?: string;
    suggestedFileName?: string;
    supportsText?: boolean;
    supportsBinary?: boolean;
    sizeKnownBeforeCreation?: boolean;
    estimatedSizeBytes?: number;
  };
}
export interface ArtifactResult {
  metadata: {
    artifactId?: string;
    providerId?: string;
    name: string;
    kind: string;
    mimeType: string;
    fileName: string;
    sizeBytes?: number;
    [key: string]: unknown;
  };
  payload:
    | string
    | Uint8Array
    | ArrayBuffer
    | { text: string }
    | { bytes: Uint8Array }
    | { base64: string };
}
export interface ArtifactRegistration {
  id: string;
  ready: Promise<{ success: boolean; id: string }>;
  unregister(): { success: boolean };
}
export class AnsightElectron extends EventEmitter {
  constructor(options: AnsightElectronOptions);
  readonly connected: boolean;
  registerArtifactProvider(provider: ArtifactProvider): ArtifactRegistration;
  registerArtifactProviders(
    providers: ArtifactProvider[],
  ): ArtifactRegistration[];
  unregisterArtifactProvider(providerId: string): { success: boolean };
  listRegisteredArtifactProviders(): ArtifactProvider["descriptor"][];
  clearArtifactProviders(): { success: boolean };
  start(): Promise<void>;
  readonly isOfflineCaptureRecording: boolean;
  readonly offlineCaptureLastError: string | null;
  setOfflineCaptureEnabled(enabled: boolean): Promise<void>;
  transferOfflineCaptures(): Promise<void>;
  stop(): Promise<void>;
  attachWindow(window: BrowserWindow): (() => void) | undefined;
  screenViewed(
    name: string,
    details?: Record<string, string>,
    channel?: number,
  ): void;
  /** Explicit app state; pass null to restore automatic attached-window lifecycle tracking. */
  setAppLifecycleState(
    state: "unknown" | "foreground" | "background" | null,
    changedAtUtc?: string,
  ): void;
  readonly isFramesPerSecondEnabled: boolean;
  enableFramesPerSecond(): Promise<void>;
  disableFramesPerSecond(): Promise<void>;
  readonly isTouchCaptureEnabled: boolean;
  enableTouchCapture(): Promise<void>;
  disableTouchCapture(): Promise<void>;
  /** Synchronous guard; only literal true permits capture. Errors fail closed. */
  setTouchCaptureGuard(callback: (() => boolean) | null): Promise<void>;
  recordNetworkRequest(request: NetworkRequest): {
    success: boolean;
    message: string;
    id?: string;
  };
  setNetworkCaptureRedactionEnabled(enabled: boolean): void;
  metric(value: number, channel?: number): boolean;
  recordedMetrics(): Array<{
    channel: number;
    value: number;
    capturedAtUtc: string;
  }>;
  recordedEvents(): Array<{
    id: string;
    label: string;
    eventType: string;
    details: string;
    capturedAtUtc: string;
    channel: number;
  }>;
  /** Clears local readback only; retained host and offline evidence is preserved. */
  clear(): void;
  snapshot(): {
    initialized: boolean;
    active: boolean;
    sessionOpen: boolean;
    metricsRecorded: number;
    eventsRecorded: number;
    lastMetric: unknown;
    lastEvent: unknown;
    registeredTools: number;
    frameRateCaptureActive: boolean;
    touchCaptureEnabled: boolean;
    lifecycleState: string;
    currentScreen: {
      name: string;
      details: Record<string, string>;
      capturedAtUtc: string;
    } | null;
    channels: object[];
  };
  event(label: string, details?: Record<string, unknown>, type?: string): void;
  setSessionProperties(properties: Record<string, unknown>): Promise<void>;
  publishArtifact(
    metadata: {
      artifactId: string;
      providerId: string;
      name: string;
      kind: string;
      mimeType: string;
      fileName: string;
      [key: string]: unknown;
    },
    data: Buffer | string,
  ): Promise<unknown>;
  log(message: string, details?: Record<string, unknown>): void;
  registerRoot(
    id: string,
    value: object,
    metadata?: { displayName?: string; description?: string; hints?: string[] },
  ): () => void;
  registerTool(
    descriptor: ToolDescriptor,
    handler: (args: Record<string, unknown>) => unknown | Promise<unknown>,
  ): () => boolean;
}
export function createAnsight(options: AnsightElectronOptions): AnsightElectron;
