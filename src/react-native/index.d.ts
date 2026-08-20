export type AnsightLifecycleState = "unknown" | "foreground" | "background";
export type AnsightToolScope = "read" | "write" | "delete" | "Read" | "Write" | "Delete";
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

export interface AnsightReactNativeMemoryOptions {
  enabled?: boolean;
  jsHeap?: boolean;
  jsHeapUsed?: boolean;
  jsHeapTotal?: boolean;
}

export interface AnsightSessionJpegCaptureOptions {
  intervalMilliseconds?: number;
  quality?: number;
  maxWidth?: number;
  captureGpuBackedSurfaces?: boolean;
  captureKeyboardPresence?: boolean;
  mode?: "screenshotOnly" | "screenshotAndVisualTree" | "screenshotWithVisualTreeOnTouch";
}

export interface AnsightTouchCaptureOptions {
  captureMoveEvents?: boolean;
  captureCancelEvents?: boolean;
  moveCaptureDistanceThreshold?: number;
  moveCaptureFramesPerSecond?: number;
}

export interface AnsightNativeToolRoot {
  alias: string;
  path: string;
}

export interface AnsightFileSystemToolsOptions {
  additionalRoots?: AnsightNativeToolRoot[];
}

export interface AnsightDatabaseToolsOptions {
  additionalRoots?: AnsightNativeToolRoot[];
  includePlatformRoots?: boolean;
}

export interface AnsightPreferencesToolsOptions {
  defaultStore?: string;
  allowedStores?: string[];
  allowedKeys?: string[];
  allowedKeyPrefixes?: string[];
}

export interface AnsightReflectionToolsOptions {
  includeBuiltInRoots?: boolean;
  allowedRootIds?: string[];
  allowedTypePrefixes?: string[];
}

export interface AnsightSecureStorageToolsOptions {
  appleService?: string;
  preferencesName?: string;
  allowedKeys?: string[];
  allowedKeyPrefixes?: string[];
  allowedPrefixes?: string[];
}

export interface AnsightVisualTreeToolsOptions {
  enabled?: boolean;
}

export interface AnsightRemoteToolsOptions {
  visualTree?: boolean | AnsightVisualTreeToolsOptions;
  fileSystem?: AnsightFileSystemToolsOptions;
  database?: AnsightDatabaseToolsOptions;
  preferences?: AnsightPreferencesToolsOptions;
  reflection?: AnsightReflectionToolsOptions;
  secureStorage?: AnsightSecureStorageToolsOptions;
}

export interface AnsightOptions {
  /**
   * Applies the native iOS/Android all-in-one defaults when true.
   *
   * This is not a master SDK enable switch and does not inspect the app's
   * runtime environment. Use your app's own condition, such as
   * React Native's `__DEV__`, and configure `toolGuard`, capture options,
   * `hostAutoProbe`, and `hostConnection` explicitly for the workflow.
   */
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
  reactNativeMemory?: false | AnsightReactNativeMemoryOptions;
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

export interface AnsightCrashCandidate {
  runtime?: string;
  kind?: string;
  message?: string;
  stack?: string;
  fatal?: boolean;
  metadata?: string | Record<string, string>;
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

export interface AnsightHostConnectionStatusSnapshot {
  status: AnsightHostConnectionStatus;
  capabilities: AnsightHostConnectionCapabilities;
}

export interface AnsightLogEntry {
  level: "debug" | "info" | "warning" | "error" | string;
  message: string;
  platform?: "ios" | "android" | string;
  error?: string;
  [key: string]: unknown;
}

export interface AnsightSubscription {
  remove(): void;
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

export type AnsightCurrentOptions = AnsightOptions & Record<string, unknown>;

export class AnsightOptionsBuilder {
  constructor(options?: AnsightOptions);
  withNativeAllInOneDefaults(): this;
  withAnsightDefaults(): this;
  withAnsightSdk(configure?: (builder: this) => void): this;
  withSampleFrequencyMilliseconds(sampleFrequencyMilliseconds: number): this;
  withFramesPerSecond(): this;
  withoutFramesPerSecond(): this;
  withBatteryLevel(): this;
  withoutBatteryLevel(): this;
  withOpenFileHandleTracking(): this;
  withoutOpenFileHandleTracking(): this;
  withJniReferenceCountTracking(): this;
  withoutJniReferenceCountTracking(): this;
  withRetentionPeriodSeconds(retentionPeriodSeconds: number): this;
  withAdditionalChannels(additionalChannels: AnsightChannel[]): this;
  addAdditionalChannel(additionalChannel: AnsightChannel): this;
  withDefaultMemoryChannels(defaultMemoryChannels: NonNullable<AnsightOptions["defaultMemoryChannels"]>): this;
  withoutDefaultMemoryChannels(defaultMemoryChannels: NonNullable<AnsightOptions["defaultMemoryChannels"]>): this;
  withReactNativeMemoryProfiling(options?: AnsightReactNativeMemoryOptions): this;
  withoutReactNativeMemoryProfiling(): this;
  withSessionJpegCapture(options?: AnsightSessionJpegCaptureOptions): this;
  withSessionJpegCapture(
    intervalMilliseconds: number,
    quality?: number,
    maxWidth?: number | null,
    captureGpuBackedSurfaces?: boolean,
    mode?: "screenshotOnly" | "screenshotAndVisualTree" | "screenshotWithVisualTreeOnTouch",
    captureKeyboardPresence?: boolean
  ): this;
  withoutSessionJpegCapture(): this;
  withTouchCapture(touchCapture?: AnsightTouchCaptureOptions): this;
  withoutTouchCapture(): this;
  withCrashCapture(crashCapture?: AnsightCrashCaptureOptions): this;
  withoutCrashCapture(): this;
  withLifecycleCapture(lifecycleCapture?: NonNullable<AnsightOptions["lifecycleCapture"]>): this;
  withToolGuard(toolGuard: NonNullable<AnsightOptions["toolGuard"]>): this;
  withToolsDisabled(): this;
  withReadOnlyToolAccess(): this;
  withReadWriteToolAccess(): this;
  withAllToolAccess(): this;
  registerCustomProperty(group: string, key: string, value: unknown): this;
  removeCustomProperty(group: string, key: string): this;
  clearCustomProperties(): this;
  withHostAutoProbe(hostAutoProbe?: NonNullable<AnsightOptions["hostAutoProbe"]>): this;
  withoutHostAutoProbe(): this;
  withHostConnection(hostConnection?: NonNullable<AnsightOptions["hostConnection"]>): this;
  configureHostConnection(
    configure: (
      hostConnection: NonNullable<AnsightOptions["hostConnection"]>
    ) => NonNullable<AnsightOptions["hostConnection"]> | void
  ): this;
  withBundledHostConnection(options?: {
    bundledConfigJson?: string;
  }): this;
  withHostConnectionDiscoveryPort(discoveryPort: number): this;
  withCellularHostConnections(allow?: boolean): this;
  withHostConnectionProfileRetentionSeconds(connectionProfileRetentionSeconds: number): this;
  withSecureStorage(secureStorage?: NonNullable<AnsightOptions["secureStorage"]>): this;
  withRemoteTools(remoteTools?: AnsightRemoteToolsOptions): this;
  withVisualTreeTools(options?: boolean | AnsightVisualTreeToolsOptions): this;
  withoutVisualTreeTools(): this;
  withFileSystemTools(options?: AnsightFileSystemToolsOptions): this;
  withDatabaseTools(options?: AnsightDatabaseToolsOptions): this;
  withPreferencesTools(options?: AnsightPreferencesToolsOptions): this;
  withReflectionTools(options?: AnsightReflectionToolsOptions): this;
  build(): AnsightOptions;
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

export type AnsightToolHandler = (
  args: Record<string, string>,
  context: {
    requestId: string;
    toolId: string;
    nativeRequestId?: string;
    sessionId?: string;
    platform: "ios" | "android" | string;
  }
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

export interface AnsightArtifactContentDescriptor {
  supportedMimeTypes: string[];
  defaultMimeType?: string | null;
  suggestedFileName?: string | null;
  supportsText?: boolean;
  supportsBinary?: boolean;
  sizeKnownBeforeCreation?: boolean;
  estimatedSizeBytes?: number | null;
}

export interface AnsightArtifactDefinition {
  id: string;
  name: string;
  description?: string;
  kind: string;
  category: string;
  content?: AnsightArtifactContentDescriptor;
  mimeType?: string;
  fileName?: string;
  estimatedSizeBytes?: number | null;
  argumentsSchema?: object;
  security?: AnsightToolSecurity;
  tags?: string[];
  metadata?: Record<string, string>;
}

export interface AnsightArtifactMetadata {
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
  metadata: AnsightArtifactMetadata;
  payload: AnsightArtifactPayload;
}

export interface AnsightArtifactQueryContext {
  toolRequestId?: string;
  sessionId?: string;
  queriedAtUtc: string;
}

export interface AnsightArtifactRequestContext {
  toolRequestId?: string;
  sessionId?: string;
  requestedAtUtc: string;
}

export interface AnsightArtifactRequest {
  providerId: string;
  artifactId: string;
  arguments: Record<string, string>;
  context: AnsightArtifactRequestContext;
}

export interface AnsightArtifactProvider {
  descriptor: AnsightArtifactProviderDescriptor;
  query?(context: AnsightArtifactQueryContext): AnsightArtifactDefinition[] | Promise<AnsightArtifactDefinition[]>;
  queryArtifacts?(context: AnsightArtifactQueryContext): AnsightArtifactDefinition[] | Promise<AnsightArtifactDefinition[]>;
  create?(request: AnsightArtifactRequest): AnsightArtifactResult | Promise<AnsightArtifactResult>;
  createArtifact?(request: AnsightArtifactRequest): AnsightArtifactResult | Promise<AnsightArtifactResult>;
}

export interface AnsightArtifactProviderRegistration {
  id: string;
  ready: Promise<AnsightOperationResult & { id?: string }>;
  unregister(): Promise<AnsightOperationResult & { id?: string }>;
}

export interface ReactNavigationTracker {
  onReady(): void;
  onStateChange(): void;
}

export interface AnsightReactToolsOptions {
  includeBounds?: boolean;
  includeProps?: boolean;
  includeState?: boolean;
  maxDepth?: number;
  maxNodes?: number;
  navigationRef?: unknown;
  enableActions?: boolean;
  allowedActionProps?: string[];
}

export interface AnsightReactToolsRegistration {
  ids: string[];
  ready: Promise<AnsightOperationResult[]>;
  unregister(): Promise<AnsightOperationResult[]>;
}

export interface AnsightConnectOptions {
  clientName?: string;
  expectedAppId?: string;
  hostAddressOverride?: string;
}

export interface AnsightScanPairingOptions extends AnsightConnectOptions {
  title?: string;
}

export interface AnsightOpenSessionOptions extends AnsightConnectOptions {}

export interface AnsightSavePairingOptions {
  expectedAppId?: string;
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

export function createOptionsBuilder(options?: AnsightOptions): AnsightOptionsBuilder;
export function initialize(options?: AnsightOptions): Promise<AnsightDebugSnapshot>;
export function initializeAndActivate(options?: AnsightOptions): Promise<AnsightDebugSnapshot>;
export function activate(): Promise<AnsightDebugSnapshot>;
export function deactivate(): Promise<AnsightDebugSnapshot>;
export function clear(): Promise<AnsightDebugSnapshot>;
export function registerMetricChannel(channel: AnsightChannel): Promise<AnsightDebugSnapshot>;
export function metric(value: number, channel?: number): Promise<AnsightDebugSnapshot>;
export function recordMetric(value: number, channel?: number): Promise<AnsightDebugSnapshot>;
export function event(input: string | { label: string; type?: string; details?: string; channel?: number }): Promise<AnsightDebugSnapshot>;
export function recordEvent(input: string | { label: string; type?: string; details?: string; channel?: number }): Promise<AnsightDebugSnapshot>;
export function recordCrashCandidate(input?: AnsightCrashCandidate): Promise<{ candidateId?: string }>;
export function screenViewed(name: string, details?: Record<string, string>): Promise<AnsightDebugSnapshot>;
export function trackRoute(name: string, details?: Record<string, string>): Promise<AnsightDebugSnapshot>;
export function setAppLifecycleState(state: AnsightLifecycleState): Promise<AnsightDebugSnapshot>;
export function connect(pairingPayload?: string | object | null, options?: AnsightConnectOptions): Promise<AnsightHostConnectionResult>;
/** Scans a one-use Studio invite, registers this installation, and saves automatic reconnect state. */
export function enrollFromQrCode(options?: AnsightScanPairingOptions): Promise<AnsightHostConnectionResult>;
export function scanPairingQrCode(options?: AnsightScanPairingOptions): Promise<AnsightHostConnectionResult>;
export function openSession(pairingPayload: string | object, options?: AnsightOpenSessionOptions): Promise<AnsightHostConnectionResult>;
export function disconnect(): Promise<AnsightHostConnectionResult>;
export function completeSession(): Promise<AnsightOperationResult>;
export function closeSession(): Promise<AnsightOperationResult>;
export function savePairingConfig(pairingPayload: string | object, options?: AnsightSavePairingOptions): Promise<AnsightHostConnectionResult>;
export function clearSavedPairing(): Promise<AnsightHostConnectionResult>;
export function clearSavedPairingConfig(): Promise<AnsightHostConnectionResult>;
export function clearCachedSession(): Promise<AnsightOperationResult>;
export function status(): Promise<AnsightDebugSnapshot>;
export function snapshot(): Promise<AnsightDebugSnapshot>;
export function hostConnectionStatus(): Promise<AnsightHostConnectionStatus>;
export function hostConnectionCapabilities(): Promise<AnsightHostConnectionCapabilities>;
export function notifyHostConnectionConfigChanged(): Promise<AnsightHostConnectionResult>;
export function addHostConnectionStatusListener(
  listener: (
    status: AnsightHostConnectionStatus,
    capabilities: AnsightHostConnectionCapabilities,
    snapshot: AnsightHostConnectionStatusSnapshot,
  ) => void,
  options?: { emitCurrent?: boolean },
): AnsightSubscription;
export function currentOptions(): Promise<AnsightCurrentOptions>;
export function recordedMetrics(limit?: number): Promise<AnsightRecordedMetric[]>;
export function recordedEvents(limit?: number): Promise<AnsightRecordedEvent[]>;
export function sendClientLog(line: string): Promise<AnsightOperationResult>;
export function addLogListener(listener: (entry: AnsightLogEntry) => void): AnsightSubscription;
export function captureBuiltInTelemetrySample(): Promise<AnsightDebugSnapshot>;
export function isFramesPerSecondEnabled(): Promise<boolean>;
export function enableFramesPerSecond(): Promise<AnsightDebugSnapshot>;
export function disableFramesPerSecond(): Promise<AnsightDebugSnapshot>;
export function captureScreenFrame(options?: AnsightSessionJpegCaptureOptions): Promise<AnsightOperationResult>;
export function enableTouchCapture(): Promise<AnsightDebugSnapshot | AnsightOperationResult>;
export function disableTouchCapture(): Promise<AnsightDebugSnapshot | AnsightOperationResult>;
export function updateSessionProperties(properties: Record<string, Record<string, string>>): Promise<AnsightOperationResult>;
export function clearSessionProperties(): Promise<AnsightOperationResult>;
export function updateCustomProperties(properties: Record<string, Record<string, string>>): Promise<AnsightOperationResult>;
export function registerCustomProperty(group: string, key: string, value: string): Promise<AnsightOperationResult>;
export function removeCustomProperty(group: string, key: string): Promise<AnsightOperationResult>;
export function clearCustomProperties(): Promise<AnsightOperationResult>;
export function registerTool(definition: AnsightToolDefinition, handler: AnsightToolHandler): AnsightToolRegistration;
export function unregisterTool(id: string): Promise<AnsightOperationResult>;
export function registerArtifactProvider(provider: AnsightArtifactProvider): AnsightArtifactProviderRegistration;
export function registerArtifactProviders(providers: AnsightArtifactProvider[]): AnsightArtifactProviderRegistration[];
export function unregisterArtifactProvider(providerId: string): Promise<AnsightOperationResult>;
export function listRegisteredArtifactProviders(): AnsightArtifactProviderDescriptor[];
export function clearArtifactProviders(): Promise<AnsightOperationResult>;
export function listRegisteredTools(): string[];
export function clearRegisteredTools(): Promise<AnsightOperationResult>;
export function startAppStateTracking(): void;
export function stopAppStateTracking(): void;
export function installReactTools(options?: AnsightReactToolsOptions): AnsightReactToolsRegistration;
export function uninstallReactTools(): Promise<AnsightOperationResult[]>;
export function installErrorHandlers(options?: { chain?: boolean }): () => void;
export function createReactNavigationTracker(navigationRef: unknown, options?: { recordInitial?: boolean }): ReactNavigationTracker;

declare const Ansight: {
  initialize: typeof initialize;
  initializeAndActivate: typeof initializeAndActivate;
  activate: typeof activate;
  deactivate: typeof deactivate;
  clear: typeof clear;
  registerMetricChannel: typeof registerMetricChannel;
  metric: typeof metric;
  recordMetric: typeof recordMetric;
  event: typeof event;
  recordEvent: typeof recordEvent;
  recordCrashCandidate: typeof recordCrashCandidate;
  screenViewed: typeof screenViewed;
  trackRoute: typeof trackRoute;
  setAppLifecycleState: typeof setAppLifecycleState;
  connect: typeof connect;
  enrollFromQrCode: typeof enrollFromQrCode;
  scanPairingQrCode: typeof scanPairingQrCode;
  openSession: typeof openSession;
  disconnect: typeof disconnect;
  completeSession: typeof completeSession;
  closeSession: typeof closeSession;
  savePairingConfig: typeof savePairingConfig;
  clearSavedPairing: typeof clearSavedPairing;
  clearSavedPairingConfig: typeof clearSavedPairingConfig;
  clearCachedSession: typeof clearCachedSession;
  status: typeof status;
  snapshot: typeof snapshot;
  hostConnectionStatus: typeof hostConnectionStatus;
  hostConnectionCapabilities: typeof hostConnectionCapabilities;
  notifyHostConnectionConfigChanged: typeof notifyHostConnectionConfigChanged;
  addHostConnectionStatusListener: typeof addHostConnectionStatusListener;
  currentOptions: typeof currentOptions;
  recordedMetrics: typeof recordedMetrics;
  recordedEvents: typeof recordedEvents;
  sendClientLog: typeof sendClientLog;
  addLogListener: typeof addLogListener;
  captureBuiltInTelemetrySample: typeof captureBuiltInTelemetrySample;
  isFramesPerSecondEnabled: typeof isFramesPerSecondEnabled;
  enableFramesPerSecond: typeof enableFramesPerSecond;
  disableFramesPerSecond: typeof disableFramesPerSecond;
  captureScreenFrame: typeof captureScreenFrame;
  enableTouchCapture: typeof enableTouchCapture;
  disableTouchCapture: typeof disableTouchCapture;
  updateSessionProperties: typeof updateSessionProperties;
  clearSessionProperties: typeof clearSessionProperties;
  updateCustomProperties: typeof updateCustomProperties;
  registerCustomProperty: typeof registerCustomProperty;
  removeCustomProperty: typeof removeCustomProperty;
  clearCustomProperties: typeof clearCustomProperties;
  registerTool: typeof registerTool;
  unregisterTool: typeof unregisterTool;
  registerArtifactProvider: typeof registerArtifactProvider;
  registerArtifactProviders: typeof registerArtifactProviders;
  unregisterArtifactProvider: typeof unregisterArtifactProvider;
  listRegisteredArtifactProviders: typeof listRegisteredArtifactProviders;
  clearArtifactProviders: typeof clearArtifactProviders;
  listRegisteredTools: typeof listRegisteredTools;
  clearRegisteredTools: typeof clearRegisteredTools;
  createOptionsBuilder: typeof createOptionsBuilder;
  AnsightOptionsBuilder: typeof AnsightOptionsBuilder;
  startAppStateTracking: typeof startAppStateTracking;
  stopAppStateTracking: typeof stopAppStateTracking;
  installReactTools: typeof installReactTools;
  uninstallReactTools: typeof uninstallReactTools;
  installErrorHandlers: typeof installErrorHandlers;
  createReactNavigationTracker: typeof createReactNavigationTracker;
  platform: "ios" | "android" | string;
};

export default Ansight;
