import type { AnsightOperationResult } from "@ansight/capacitor";

export interface AnsightLocationOptions { enabled?: boolean; decimalPlaces?: number; minimumIntervalMilliseconds?: number; minimumDistanceMeters?: number; }
export interface AnsightLocationSample {
  latitude: number; longitude: number; altitudeMeters?: number | null; horizontalAccuracyMeters?: number | null;
  verticalAccuracyMeters?: number | null; speedMetersPerSecond?: number | null; headingDegrees?: number | null;
  timestamp?: number; sampleId?: string; correlationId?: string; runId?: string;
}
export class AnsightLocationRecorder {
  constructor(options?: AnsightLocationOptions);
  record(sample: AnsightLocationSample): Promise<AnsightOperationResult>;
  recordGeolocationPosition(position: GeolocationPosition, context?: Pick<AnsightLocationSample, "sampleId" | "correlationId" | "runId">): Promise<AnsightOperationResult>;
}
export const EVENT_TYPE: "CLIENT_LOCATION";
export const SCHEMA: "ansight.location.sample.v1";
