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
  manualHostAddress: string;
  expectedAppId?: string;
  profileOverride?: Record<string, string>;
};

export type OpenSessionResult = {
  success: boolean;
  message: string;
  sessionId?: string | null;
};

export type AnsightDebugSnapshot = {
  initialized: boolean;
  active: boolean;
  sessionOpen: boolean;
  metricsRecorded: number;
  eventsRecorded: number;
  registeredTools: number;
  sessionMessage?: string | null;
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
  scope?: string;
};

export type EventOptions = {
  type?: AnsightEventType;
  details?: string;
  channel?: number;
  id?: string;
};
