export type AnsightChannel = {
  id: number;
  name: string;
  colorHex?: string | null;
};

export type AnsightOptions = {
  sampleFrequencyMilliseconds?: number;
  retentionPeriodSeconds?: number;
  enableFramesPerSecond?: boolean;
  additionalChannels?: AnsightChannel[];
  toolAccess?: "disabled" | "read" | "readonly" | "full" | "all";
};

export type AnsightEventType =
  | "Event"
  | "Debug"
  | "Info"
  | "Warning"
  | "Error"
  | "Exception"
  | "Gc"
  | "Navigation";

export type PairingOpenOptions = {
  clientName: string;
  expectedAppId?: string;
  profileOverride?: Record<string, string>;
};

export type OpenSessionResult = {
  success: boolean;
  message: string;
  sessionId?: string | null;
  configId?: string | null;
  appId?: string | null;
  resolvedHostAddress?: string | null;
  usedEmbeddedDeveloperPairing?: boolean;
  discoverySource?: string | null;
};

export type AnsightDebugSnapshot = {
  initialized: boolean;
  active: boolean;
  sessionOpen: boolean;
  metricsRecorded: number;
  eventsRecorded: number;
  registeredTools: number;
  executableTools: number;
  toolDiscoveryEnabled: boolean;
  toolExecutionEnabled: boolean;
  embeddedDeveloperPairingAvailable: boolean;
  detectedBundledTools: string[];
  sessionMessage?: string | null;
  lastPairingConfigId?: string | null;
  resolvedHostAddress?: string | null;
  lastMetric?: {
    value: number;
    channel: number;
    capturedAtEpochMs: number;
  } | null;
  lastEvent?: {
    id: string;
    label: string;
    type: AnsightEventType;
    details?: string | null;
    channel: number;
    capturedAtEpochMs: number;
  } | null;
};

export type AnsightToolDescriptor = {
  id: string;
  name: string;
  description?: string;
  category?: string;
  scope?: string;
  keywords?: string;
};

export type EventOptions = {
  type?: AnsightEventType;
  details?: string;
  channel?: number;
  id?: string;
};
