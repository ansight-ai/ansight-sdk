"use strict";

const Ansight = require("@ansight/react-native");

const EVENT_TYPE = "CLIENT_LOCATION";
const SCHEMA = "ansight.location.sample.v1";

class AnsightLocationRecorder {
  constructor(options = {}) {
    this.options = {
      enabled: options.enabled === true,
      decimalPlaces: clamp(options.decimalPlaces ?? 5, 0, 7),
      minimumIntervalMilliseconds: Math.max(0, options.minimumIntervalMilliseconds ?? 1000),
      minimumDistanceMeters: Math.max(0, options.minimumDistanceMeters ?? 1),
    };
    this.lastSample = null;
  }

  async record(sample) {
    if (!this.options.enabled) return { success: false, message: "Observed location capture is disabled." };
    validate(sample);
    const normalized = normalize(sample, this.options.decimalPlaces);
    if (this.lastSample && shouldSuppress(this.lastSample, normalized, this.options)) {
      return { success: true, message: "Observed location suppressed by sampling controls." };
    }
    this.lastSample = normalized;
    return Ansight.sendSessionEvent(EVENT_TYPE, {
      schema: SCHEMA,
      sampleId: normalized.sampleId,
      capturedAtUtc: new Date(normalized.timestamp).toISOString(),
      source: "app_observed",
      latitude: normalized.latitude,
      longitude: normalized.longitude,
      ...optional("altitudeMeters", normalized.altitudeMeters),
      ...optional("horizontalAccuracyMeters", normalized.horizontalAccuracyMeters),
      ...optional("verticalAccuracyMeters", normalized.verticalAccuracyMeters),
      ...optional("speedMetersPerSecond", normalized.speedMetersPerSecond),
      ...optional("headingDegrees", normalized.headingDegrees),
      ...optional("correlationId", normalized.correlationId),
      ...optional("runId", normalized.runId),
    });
  }

  recordExpoLocation(location, context = {}) {
    if (!location || !location.coords) throw new TypeError("Expo location must include coords.");
    return this.record({
      latitude: location.coords.latitude,
      longitude: location.coords.longitude,
      altitudeMeters: location.coords.altitude,
      horizontalAccuracyMeters: location.coords.accuracy,
      verticalAccuracyMeters: location.coords.altitudeAccuracy,
      speedMetersPerSecond: location.coords.speed,
      headingDegrees: location.coords.heading,
      timestamp: location.timestamp,
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

function normalize(sample, decimalPlaces) {
  return {
    ...sample,
    latitude: round(sample.latitude, decimalPlaces),
    longitude: round(sample.longitude, decimalPlaces),
    timestamp: Number.isFinite(sample.timestamp) ? sample.timestamp : Date.now(),
    sampleId: text(sample.sampleId) || randomId(),
    correlationId: text(sample.correlationId),
    runId: text(sample.runId),
  };
}

function shouldSuppress(previous, current, options) {
  return current.timestamp - previous.timestamp < options.minimumIntervalMilliseconds ||
    distanceMeters(previous, current) < options.minimumDistanceMeters;
}

function distanceMeters(first, second) {
  const radians = (value) => value * Math.PI / 180;
  const dLat = radians(second.latitude - first.latitude);
  const dLon = radians(second.longitude - first.longitude);
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(radians(first.latitude)) *
    Math.cos(radians(second.latitude)) * Math.sin(dLon / 2) ** 2;
  return 2 * 6371000 * Math.asin(Math.sqrt(a));
}

function round(value, places) { const scale = 10 ** places; return Math.round(value * scale) / scale; }
function clamp(value, min, max) { return Math.min(max, Math.max(min, Math.trunc(value))); }
function text(value) { return typeof value === "string" && value.trim() ? value.trim() : undefined; }
function optional(key, value) { return value == null || !Number.isFinite(value) && typeof value === "number" ? {} : { [key]: value }; }
function randomId() { return globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`; }

module.exports = { AnsightLocationRecorder, EVENT_TYPE, SCHEMA };
