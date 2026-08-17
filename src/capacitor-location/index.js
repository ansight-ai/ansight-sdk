import { sendSessionEvent } from "@ansight/capacitor";

export const EVENT_TYPE = "CLIENT_LOCATION";
export const SCHEMA = "ansight.location.sample.v1";

export class AnsightLocationRecorder {
  constructor(options = {}) {
    this.options = {
      enabled: options.enabled === true,
      decimalPlaces: Math.min(7, Math.max(0, Math.trunc(options.decimalPlaces ?? 5))),
      minimumIntervalMilliseconds: Math.max(0, options.minimumIntervalMilliseconds ?? 1000),
      minimumDistanceMeters: Math.max(0, options.minimumDistanceMeters ?? 1),
    };
    this.lastSample = null;
  }

  async record(sample) {
    validate(sample);
    if (!this.options.enabled) return { success: false, message: "Observed location capture is disabled." };
    const normalized = normalize(sample, this.options.decimalPlaces);
    if (this.lastSample && shouldSuppress(this.lastSample, normalized, this.options)) {
      return { success: true, message: "Observed location suppressed by sampling controls." };
    }
    this.lastSample = normalized;
    return sendSessionEvent(EVENT_TYPE, compact({
      schema: SCHEMA,
      sampleId: normalized.sampleId,
      capturedAtUtc: new Date(normalized.timestamp).toISOString(),
      source: "app_observed",
      latitude: normalized.latitude,
      longitude: normalized.longitude,
      altitudeMeters: normalized.altitudeMeters,
      horizontalAccuracyMeters: normalized.horizontalAccuracyMeters,
      verticalAccuracyMeters: normalized.verticalAccuracyMeters,
      speedMetersPerSecond: normalized.speedMetersPerSecond,
      headingDegrees: normalized.headingDegrees,
      correlationId: normalized.correlationId,
      runId: normalized.runId,
    }));
  }

  recordGeolocationPosition(position, context = {}) {
    if (!position?.coords) throw new TypeError("Geolocation position must include coords.");
    return this.record({
      latitude: position.coords.latitude,
      longitude: position.coords.longitude,
      altitudeMeters: position.coords.altitude,
      horizontalAccuracyMeters: position.coords.accuracy,
      verticalAccuracyMeters: position.coords.altitudeAccuracy,
      speedMetersPerSecond: position.coords.speed,
      headingDegrees: position.coords.heading,
      timestamp: position.timestamp,
      ...context,
    });
  }
}

function validate(sample) {
  if (!sample || !Number.isFinite(sample.latitude) || sample.latitude < -90 || sample.latitude > 90 ||
      !Number.isFinite(sample.longitude) || sample.longitude < -180 || sample.longitude > 180) {
    throw new RangeError("Observed location coordinates are invalid.");
  }
}
function normalize(sample, places) {
  const scale = 10 ** places;
  return { ...sample, latitude: Math.round(sample.latitude * scale) / scale,
    longitude: Math.round(sample.longitude * scale) / scale,
    timestamp: Number.isFinite(sample.timestamp) ? sample.timestamp : Date.now(),
    sampleId: clean(sample.sampleId) || globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`,
    correlationId: clean(sample.correlationId), runId: clean(sample.runId) };
}
function shouldSuppress(a, b, options) {
  return b.timestamp - a.timestamp < options.minimumIntervalMilliseconds || distance(a, b) < options.minimumDistanceMeters;
}
function distance(a, b) {
  const r = (v) => v * Math.PI / 180; const dLat = r(b.latitude - a.latitude); const dLon = r(b.longitude - a.longitude);
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(r(a.latitude)) * Math.cos(r(b.latitude)) * Math.sin(dLon / 2) ** 2;
  return 12742000 * Math.asin(Math.sqrt(h));
}
function clean(value) { return typeof value === "string" && value.trim() ? value.trim() : undefined; }
function compact(value) { return Object.fromEntries(Object.entries(value).filter(([, item]) => item != null && (typeof item !== "number" || Number.isFinite(item)))); }
