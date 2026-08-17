import type { AnsightOperationResult } from "@ansight/react-native";

export interface AnsightLocationOptions {
  enabled?: boolean;
  decimalPlaces?: number;
  minimumIntervalMilliseconds?: number;
  minimumDistanceMeters?: number;
}

export interface AnsightLocationSample {
  latitude: number;
  longitude: number;
  altitudeMeters?: number | null;
  horizontalAccuracyMeters?: number | null;
  verticalAccuracyMeters?: number | null;
  speedMetersPerSecond?: number | null;
  headingDegrees?: number | null;
  timestamp?: number;
  sampleId?: string;
  correlationId?: string;
  runId?: string;
}

export interface ExpoLocationObject {
  coords: {
    latitude: number;
    longitude: number;
    altitude?: number | null;
    accuracy?: number | null;
    altitudeAccuracy?: number | null;
    speed?: number | null;
    heading?: number | null;
  };
  timestamp?: number;
}

export class AnsightLocationRecorder {
  constructor(options?: AnsightLocationOptions);
  record(sample: AnsightLocationSample): Promise<AnsightOperationResult>;
  recordExpoLocation(location: ExpoLocationObject, context?: Pick<AnsightLocationSample, "sampleId" | "correlationId" | "runId">): Promise<AnsightOperationResult>;
}

export const EVENT_TYPE: "CLIENT_LOCATION";
export const SCHEMA: "ansight.location.sample.v1";
