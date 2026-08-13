import type { PluginListenerHandle } from "@capacitor/core";

export type AnsightLifecycleState = "unknown" | "foreground" | "background";
export type AnsightToolScope =
  "read" | "write" | "delete" | "Read" | "Write" | "Delete";
export type AnsightToolSecurityLevel =
  | "unspecified"
  | "low"
  | "medium"
  | "moderate"
  | "high"
  | "critical"
  | "Unspecified"
  | "Low"
  | "Medium"
  | "Moderate"
  | "High"
  | "Critical";

export interface AnsightChannel {
  id: number;
  name: string;
  unit?: string;
  type?: string;
  colorHex?: string;
  source?: string;
  group?: string;
  kind?: string;
}

export interface AnsightSessionJpegCaptureOptions {
  intervalMilliseconds?: number;
  quality?: number;
  maxWidth?: number | null;
  captureGpuBackedSurfaces?: boolean;
  mode?: "screenshotOnly" | "screenshotAndVisualTree";
}

export interface AnsightTouchCaptureOptions {
  captureMoveEvents?: boolean;
  captureCancelEvents?: boolean;
  moveCaptureDistanceThreshold?: number;
  moveCaptureFramesPerSecond?: number;
}

export interface AnsightCrashCaptureOptions {
  enabled?: boolean;
  studioHandoffEnabled?: boolean;
  offlineCaptureAttachmentEnabled?: boolean;
  maximumPendingReports?: number;
  retentionDays?: number;
  maximumBreadcrumbs?: number;
  maximumTraceBytes?: number;
}

export interface AnsightNativeToolRoot {
  alias: string;
  path: string;
}

export interface AnsightRemoteToolsOptions {
  visualTree?: boolean | { enabled?: boolean };
  fileSystem?: { additionalRoots?: AnsightNativeToolRoot[] };
  database?: {
    additionalRoots?: AnsightNativeToolRoot[];
    includePlatformRoots?: boolean;
  };
  preferences?: {
    defaultStore?: string;
    allowedStores?: string[];
    allowedKeys?: string[];
    allowedKeyPrefixes?: string[];
  };
  reflection?: {
    includeBuiltInRoots?: boolean;
    allowedRootIds?: string[];
    allowedTypePrefixes?: string[];
  };
  secureStorage?: {
    appleService?: string;
    preferencesName?: string;
    allowedKeys?: string[];
    allowedKeyPrefixes?: string[];
    allowedPrefixes?: string[];
  };
}

export interface AnsightOptions {
  useNativeAllInOneDefaults?: boolean;
  clientName?: string;
  sampleFrequencyMilliseconds?: number;
  retentionPeriodSeconds?: number;
  enableFramesPerSecond?: boolean;
  enableBatteryLevel?: boolean;
  enableOpenFileHandleTracking?: boolean;
  enableJniReferenceCountTracking?: boolean;
  defaultMemoryChannels?: {
    managedHeap?: boolean;
    physicalFootprint?: boolean;
    residentSetSize?: boolean;
    javaHeap?: boolean;
    nativeHeap?: boolean;
    rss?: boolean;
  };
  sessionJpegCapture?: false | AnsightSessionJpegCaptureOptions;
  touchCapture?: false | AnsightTouchCaptureOptions;
  crashCapture?: false | AnsightCrashCaptureOptions;
  lifecycleCapture?: {
    enabled?: boolean;
    captureAppLifecycle?: boolean;
    captureScreenViews?: boolean;
    minimumScreenViewIntervalMilliseconds?: number;
  };
  toolGuard?: "disabled" | "readOnly" | "readWrite" | "full" | "fullAccess";
  customProperties?: Record<string, Record<string, string>>;
  hostAutoProbe?: {
    enabled?: boolean;
    initialDelayMilliseconds?: number;
    probeIntervalMilliseconds?: number;
    reconnectDelayMilliseconds?: number;
    clientName?: string;
  };
  hostConnection?: {
    savedConfigKey?: string;
    bundledConfigJson?: string;
    discoveryPort?: number;
    allowCellularConnections?: boolean;
    connectionProfileRetentionSeconds?: number;
  };
  secureStorage?: {
    preferencesName?: string;
    allowedKeys?: string[];
    allowedPrefixes?: string[];
  };
  remoteTools?: AnsightRemoteToolsOptions;
  additionalChannels?: AnsightChannel[];
  lifecycle?: boolean;
  domTools?: boolean | AnsightDomToolsOptions;
  errorCapture?: boolean | AnsightErrorCaptureOptions;
}

export interface AnsightOperationResult {
  success: boolean;
  message: string;
  [key: string]: unknown;
}

export interface AnsightHostConnectionStatus {
  isRuntimeActive: boolean;
  isConnected: boolean;
  connectionState: string;
  hasCachedSession: boolean;
  hasSavedConfig: boolean;
  hasBundledConfig: boolean;
  summaryKind: string;
  summaryMessage: string;
  [key: string]: unknown;
}

export interface AnsightHostConnectionCapabilities {
  canConnectUsingSavedConfig: boolean;
  canConnectUsingBundledConfig: boolean;
  canChooseConfigFile: boolean;
  canScanConfigQrCode: boolean;
  canClearSavedConfigs: boolean;
  [key: string]: unknown;
}

export interface AnsightHostConnectionResult extends AnsightOperationResult {
  kind?: string;
  source?: string;
  reasonCode?: string;
  accepted?: boolean;
  sessionId?: string;
  configId?: string;
  appId?: string;
  resolvedHostAddress?: string;
  discoverySource?: string;
  hostId?: string;
  hostName?: string;
}

export interface AnsightScreenSnapshot {
  name: string;
  capturedAtUtc: string;
  details?: Record<string, string>;
}

export interface AnsightRecordedMetric {
  value: number;
  capturedAtUtc: string;
  capturedAtEpochMs: number;
  channel: number;
  sequence: number;
}

export interface AnsightRecordedEvent {
  id: string;
  label: string;
  type: string;
  details?: string;
  capturedAtUtc: string;
  capturedAtEpochMs: number;
  externalId?: string;
  channel: number;
  sequence: number;
}

export interface AnsightDebugSnapshot {
  initialized: boolean;
  active: boolean;
  sessionOpen: boolean;
  lifecycleState?: string;
  lifecycleChangedAtUtc?: string;
  metricsRecorded: number;
  eventsRecorded: number;
  touchesRecorded?: number;
  touchesCaptured?: number;
  touchesSent?: number;
  registeredTools: number;
  executableTools?: number;
  sessionMessage?: string;
  connectionStatus: AnsightHostConnectionStatus;
  channels: AnsightChannel[];
  lastMetric?: AnsightRecordedMetric;
  lastEvent?: AnsightRecordedEvent;
  currentScreen?: AnsightScreenSnapshot;
  [key: string]: unknown;
}

export interface AnsightLogEntry {
  level: "debug" | "info" | "warning" | "error" | string;
  message: string;
  platform?: "ios" | "android" | string;
  error?: string;
  [key: string]: unknown;
}

export interface AnsightSubscription {
  remove(): Promise<void> | void;
}

export interface AnsightConnectOptions {
  clientName?: string;
  expectedAppId?: string;
  hostAddressOverride?: string;
}

export interface AnsightQrPairingOptions extends AnsightConnectOptions {
  title?: string;
}

export interface AnsightToolSecurity {
  level?: AnsightToolSecurityLevel;
  summary?: string;
  implications?: string[];
}

export interface AnsightToolDefinition {
  id: string;
  name: string;
  description?: string;
  category?: string;
  scope?: AnsightToolScope;
  keywords?: string | string[];
  argumentsSchema?: object;
  resultSchema?: object;
  security?: AnsightToolSecurity;
  timeoutMilliseconds?: number;
}

export type AnsightToolResult =
  | { success: true; message?: string; result?: unknown }
  | { success: false; message: string; errorCode?: string; result?: unknown };

export interface AnsightToolCall {
  requestId: string;
  toolId: string;
  arguments: Record<string, string>;
  nativeRequestId?: string;
  sessionId?: string;
  platform: "ios" | "android" | "web" | string;
}

export type AnsightToolHandler = (
  args: Record<string, string>,
  context: AnsightToolCall,
) => AnsightToolResult | Promise<AnsightToolResult>;

export interface AnsightToolRegistration {
  id: string;
  ready: Promise<AnsightOperationResult>;
  unregister(): Promise<AnsightOperationResult>;
}

export interface AnsightArtifactProviderDescriptor {
  id: string;
  name: string;
  description?: string | null;
  category?: string;
  tags?: string[];
  metadata?: Record<string, string>;
}

export interface AnsightArtifactDefinition {
  id: string;
  name: string;
  description?: string;
  kind: string;
  category: string;
  mimeType?: string;
  fileName?: string;
  estimatedSizeBytes?: number | null;
  argumentsSchema?: object;
  security?: AnsightToolSecurity;
  tags?: string[];
  metadata?: Record<string, string>;
  content?: {
    supportedMimeTypes: string[];
    defaultMimeType?: string | null;
    suggestedFileName?: string | null;
    supportsText?: boolean;
    supportsBinary?: boolean;
    sizeKnownBeforeCreation?: boolean;
    estimatedSizeBytes?: number | null;
  };
}

export type AnsightArtifactPayload =
  | string
  | number[]
  | ArrayBuffer
  | Uint8Array
  | {
      text?: string;
      base64?: string;
      bytes?: number[] | ArrayBuffer | Uint8Array;
      data?: number[] | ArrayBuffer | Uint8Array;
      sizeBytes?: number;
    };

export interface AnsightArtifactResult {
  metadata: {
    artifactId: string;
    providerId: string;
    name: string;
    kind: string;
    mimeType: string;
    fileName: string;
    description?: string | null;
    sizeBytes?: number | null;
    createdAtUtc?: string;
    tags?: string[];
    metadata?: Record<string, string>;
  };
  payload: AnsightArtifactPayload;
}

export interface AnsightArtifactProvider {
  descriptor: AnsightArtifactProviderDescriptor;
  query?(context: {
    toolRequestId?: string;
    sessionId?: string;
    queriedAtUtc: string;
  }): AnsightArtifactDefinition[] | Promise<AnsightArtifactDefinition[]>;
  queryArtifacts?(context: {
    toolRequestId?: string;
    sessionId?: string;
    queriedAtUtc: string;
  }): AnsightArtifactDefinition[] | Promise<AnsightArtifactDefinition[]>;
  create?(request: {
    providerId: string;
    artifactId: string;
    arguments: Record<string, string>;
    context: {
      toolRequestId?: string;
      sessionId?: string;
      requestedAtUtc: string;
    };
  }): AnsightArtifactResult | Promise<AnsightArtifactResult>;
  createArtifact?(request: {
    providerId: string;
    artifactId: string;
    arguments: Record<string, string>;
    context: {
      toolRequestId?: string;
      sessionId?: string;
      requestedAtUtc: string;
    };
  }): AnsightArtifactResult | Promise<AnsightArtifactResult>;
}

export interface AnsightArtifactProviderRegistration {
  id: string;
  ready: Promise<AnsightOperationResult>;
  unregister(): Promise<AnsightOperationResult>;
}

export interface AnsightDomToolsOptions {
  source?: string;
  includeHidden?: boolean;
  maxDepth?: number;
  maxNodes?: number;
  includeText?: boolean;
  includeAttributes?: boolean;
  allowActions?: boolean;
}

export interface AnsightErrorCaptureOptions {
  errors?: boolean;
  unhandledRejections?: boolean;
  consoleErrors?: boolean;
}

export interface AnsightRouteTrackerOptions {
  details?: Record<string, string>;
  observeHistory?: boolean;
  resolveName?: (url: URL) => string;
}

export interface AnsightOptionsBuilderApi {
  withAnsightDefaults(): this;
  withNativeAllInOneDefaults(): this;
  withAnsightSdk(configure?: (builder: this) => void): this;
  withSampleFrequencyMilliseconds(value: number): this;
  withFramesPerSecond(): this;
  withoutFramesPerSecond(): this;
  withBatteryLevel(): this;
  withoutBatteryLevel(): this;
  withOpenFileHandleTracking(): this;
  withoutOpenFileHandleTracking(): this;
  withJniReferenceCountTracking(): this;
  withoutJniReferenceCountTracking(): this;
  withRetentionPeriodSeconds(value: number): this;
  withAdditionalChannels(channels: AnsightChannel[]): this;
  addAdditionalChannel(channel: AnsightChannel): this;
  withDefaultMemoryChannels(
    channels: NonNullable<AnsightOptions["defaultMemoryChannels"]>,
  ): this;
  withoutDefaultMemoryChannels(
    channels: NonNullable<AnsightOptions["defaultMemoryChannels"]>,
  ): this;
  withSessionJpegCapture(options?: AnsightSessionJpegCaptureOptions): this;
  withSessionJpegCapture(
    intervalMilliseconds: number,
    quality?: number,
    maxWidth?: number | null,
    captureGpuBackedSurfaces?: boolean,
    mode?: "screenshotOnly" | "screenshotAndVisualTree",
  ): this;
  withoutSessionJpegCapture(): this;
  withTouchCapture(options?: AnsightTouchCaptureOptions): this;
  withoutTouchCapture(): this;
  withCrashCapture(options?: AnsightCrashCaptureOptions): this;
  withoutCrashCapture(): this;
  withLifecycleCapture(
    options?: NonNullable<AnsightOptions["lifecycleCapture"]>,
  ): this;
  withToolGuard(toolGuard: NonNullable<AnsightOptions["toolGuard"]>): this;
  withToolsDisabled(): this;
  withReadOnlyToolAccess(): this;
  withReadWriteToolAccess(): this;
  withAllToolAccess(): this;
  withHostAutoProbe(
    options?: NonNullable<AnsightOptions["hostAutoProbe"]>,
  ): this;
  withoutHostAutoProbe(): this;
  withHostConnection(
    options?: NonNullable<AnsightOptions["hostConnection"]>,
  ): this;
  configureHostConnection(
    configure: (
      options: NonNullable<AnsightOptions["hostConnection"]>,
    ) => NonNullable<AnsightOptions["hostConnection"]> | void,
  ): this;
  withBundledHostConnection(options?: { bundledConfigJson?: string }): this;
  withHostConnectionDiscoveryPort(discoveryPort: number): this;
  withCellularHostConnections(allow?: boolean): this;
  withHostConnectionProfileRetentionSeconds(
    connectionProfileRetentionSeconds: number,
  ): this;
  withSecureStorage(
    options?: NonNullable<AnsightOptions["secureStorage"]>,
  ): this;
  withRemoteTools(options?: AnsightRemoteToolsOptions): this;
  withVisualTreeTools(options?: boolean | { enabled?: boolean }): this;
  withoutVisualTreeTools(): this;
  withFileSystemTools(
    options?: NonNullable<AnsightRemoteToolsOptions["fileSystem"]>,
  ): this;
  withDatabaseTools(
    options?: NonNullable<AnsightRemoteToolsOptions["database"]>,
  ): this;
  withPreferencesTools(
    options?: NonNullable<AnsightRemoteToolsOptions["preferences"]>,
  ): this;
  withReflectionTools(
    options?: NonNullable<AnsightRemoteToolsOptions["reflection"]>,
  ): this;
  withDomTools(options?: AnsightDomToolsOptions): this;
  withErrorCapture(options?: AnsightErrorCaptureOptions): this;
  registerCustomProperty(group: string, key: string, value: unknown): this;
  removeCustomProperty(group: string, key: string): this;
  clearCustomProperties(): this;
  build(): AnsightOptions;
}

export interface AnsightCapacitorPlugin {
  initialize(options: AnsightOptions): Promise<AnsightDebugSnapshot>;
  initializeAndActivate(options: AnsightOptions): Promise<AnsightDebugSnapshot>;
  activate(): Promise<AnsightDebugSnapshot>;
  deactivate(): Promise<AnsightDebugSnapshot>;
  clear(): Promise<AnsightDebugSnapshot>;
  registerMetricChannel(options: {
    channel: AnsightChannel;
  }): Promise<AnsightDebugSnapshot>;
  recordMetric(options: {
    value: number;
    channel: number;
  }): Promise<AnsightDebugSnapshot>;
  recordEvent(options: {
    label: string;
    type?: string;
    details?: string;
    channel?: number;
  }): Promise<AnsightDebugSnapshot>;
  recordCrashCandidate(options: {
    runtime?: string;
    kind?: string;
    message?: string;
    stack?: string;
    fatal?: boolean;
    metadata?: string;
  }): Promise<{ candidateId?: string }>;
  screenViewed(options: {
    name: string;
    details?: Record<string, string>;
  }): Promise<AnsightDebugSnapshot>;
  setAppLifecycleState(options: {
    state: AnsightLifecycleState;
  }): Promise<AnsightDebugSnapshot>;
  connect(
    options: AnsightConnectOptions & { pairingPayload?: string | null },
  ): Promise<AnsightHostConnectionResult>;
  scanPairingQrCode(
    options: AnsightQrPairingOptions,
  ): Promise<AnsightHostConnectionResult>;
  openSession(
    options: AnsightConnectOptions & { pairingPayload: string },
  ): Promise<AnsightHostConnectionResult>;
  disconnect(): Promise<AnsightHostConnectionResult>;
  completeSession(): Promise<AnsightOperationResult>;
  closeSession(): Promise<AnsightOperationResult>;
  savePairingConfig(options: {
    pairingPayload: string;
    expectedAppId?: string;
  }): Promise<AnsightHostConnectionResult>;
  clearSavedPairing(): Promise<AnsightHostConnectionResult>;
  clearCachedSession(): Promise<AnsightOperationResult>;
  notifyHostConnectionConfigChanged(): Promise<AnsightHostConnectionResult>;
  status(): Promise<AnsightDebugSnapshot>;
  snapshot(): Promise<AnsightDebugSnapshot>;
  hostConnectionStatus(): Promise<AnsightHostConnectionStatus>;
  hostConnectionCapabilities(): Promise<AnsightHostConnectionCapabilities>;
  currentOptions(): Promise<AnsightOptions & Record<string, unknown>>;
  recordedMetrics(options: {
    limit: number;
  }): Promise<{ items: AnsightRecordedMetric[] }>;
  recordedEvents(options: {
    limit: number;
  }): Promise<{ items: AnsightRecordedEvent[] }>;
  sendClientLog(options: { line: string }): Promise<AnsightOperationResult>;
  captureBuiltInTelemetrySample(): Promise<AnsightDebugSnapshot>;
  isFramesPerSecondEnabled(): Promise<{ value: boolean }>;
  enableFramesPerSecond(): Promise<AnsightDebugSnapshot>;
  disableFramesPerSecond(): Promise<AnsightDebugSnapshot>;
  captureScreenFrame(
    options: AnsightSessionJpegCaptureOptions,
  ): Promise<AnsightOperationResult>;
  enableTouchCapture(): Promise<AnsightDebugSnapshot>;
  disableTouchCapture(): Promise<AnsightDebugSnapshot>;
  updateSessionProperties(options: {
    properties: Record<string, Record<string, string>>;
  }): Promise<AnsightOperationResult>;
  clearSessionProperties(): Promise<AnsightOperationResult>;
  registerCustomProperty(options: {
    group: string;
    key: string;
    value: string;
  }): Promise<AnsightOperationResult>;
  removeCustomProperty(options: {
    group: string;
    key: string;
  }): Promise<AnsightOperationResult>;
  registerCustomTool(options: {
    definition: AnsightToolDefinition;
  }): Promise<AnsightOperationResult>;
  unregisterCustomTool(options: {
    id: string;
  }): Promise<AnsightOperationResult>;
  clearRegisteredCustomTools(): Promise<AnsightOperationResult>;
  resolveToolCall(options: {
    requestId: string;
    result: AnsightToolResult;
  }): Promise<AnsightOperationResult>;
  queueBinaryTransfer(options: {
    requestId: string;
    base64Data: string;
    chunkBytes: number;
  }): Promise<AnsightOperationResult>;
  addListener(
    eventName: "ansightToolCall",
    listener: (event: AnsightToolCall) => void,
  ): Promise<PluginListenerHandle>;
  addListener(
    eventName: "ansightLog",
    listener: (event: AnsightLogEntry) => void,
  ): Promise<PluginListenerHandle>;
  removeAllListeners(): Promise<void>;
}
