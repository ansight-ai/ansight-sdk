"use strict";
// Local readback is independent of transport queues and acknowledged offline files.
class Retention {
  constructor(seconds, now = Date.now) {
    this.seconds = seconds;
    this.now = now;
    this.metrics = [];
    this.events = [];
    this.eventBytes = 0;
  }
  prune() {
    const cutoff = this.now() - this.seconds * 1000;
    while (this.metrics.length && this.metrics[0].time < cutoff)
      this.metrics.shift();
    while (this.events.length && this.events[0].time < cutoff)
      this.eventBytes -= this.events.shift().bytes;
  }
  append(message) {
    const time = this.now();
    for (const value of message.metrics || [])
      this.metrics.push({ time, value: structuredClone(value) });
    for (const value of message.events || []) {
      const bytes = Buffer.byteLength(JSON.stringify(value));
      this.events.push({ time, bytes, value: structuredClone(value) });
      this.eventBytes += bytes;
    }
    this.prune();
    if (this.metrics.length > 100000)
      this.metrics.splice(0, this.metrics.length - 100000);
    while (this.events.length > 10000 || this.eventBytes > 16 * 1024 * 1024)
      this.eventBytes -= this.events.shift().bytes;
  }
  read(kind) {
    this.prune();
    return this[kind].map((x) => structuredClone(x.value));
  }
  clear() {
    this.metrics.length = 0;
    this.events.length = 0;
    this.eventBytes = 0;
  }
}
function normalizedTiming(options) {
  const normalize = (value, fallback, min, max) => {
    if (value == null) return fallback;
    if (!Number.isFinite(value))
      throw new Error("Timing options must be finite numbers");
    return Math.min(max, Math.max(min, Math.trunc(value)));
  };
  return {
    sampleFrequencyMilliseconds: normalize(
      options.sampleFrequencyMilliseconds ?? options.sampleIntervalMilliseconds,
      400,
      200,
      2000,
    ),
    retentionPeriodSeconds: normalize(
      options.retentionPeriodSeconds,
      120,
      60,
      3600,
    ),
  };
}
module.exports = { Retention, normalizedTiming };
