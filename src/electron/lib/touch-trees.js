"use strict";
const { randomUUID } = require("node:crypto");
// Mirrors the native touch-tree coordinator's phases and pending-trigger policy.
class TouchTreeCoordinator {
  constructor(capture, interval = 750) {
    this.capture = capture;
    this.interval = Math.max(750, interval);
    this.pointers = new Set();
    this.closed = false;
  }
  observe(touch) {
    if (this.closed) return;
    let phase;
    if (touch.action === 0) {
      const first = this.pointers.size === 0;
      this.pointers.add(touch.pointerId);
      if (first) this.gestureId = `gesture-${randomUUID()}`;
      phase = first ? "started" : "checkpoint";
    } else if (touch.action === 1) {
      this.pointers.add(touch.pointerId);
      return;
    } else if (touch.action === 2) {
      this.pointers.delete(touch.pointerId);
      phase = this.pointers.size ? "checkpoint" : "ended";
    } else if (touch.action === 3) {
      this.interrupt();
      return;
    } else return;
    const trigger = {
      gestureId: this.gestureId || `gesture-${randomUUID()}`,
      gesturePhase: phase,
      touchAction: touch.action === 0 ? "down" : "up",
      touchCapturedAtUtc: touch.capturedAtUtc,
    };
    if (
      !this.pending ||
      phase === "started" ||
      (this.pending.gesturePhase !== "started" && phase === "ended")
    )
      this.pending = trigger;
    if (!this.timer && !this.capturing) {
      this.timer = setTimeout(() => this.run(), 0);
      this.timer.unref?.();
    }
  }
  async run() {
    this.timer = null;
    if (this.closed || !this.pending) return;
    const trigger = this.pending;
    this.pending = null;
    this.capturing = true;
    try {
      await this.capture(trigger);
    } finally {
      this.capturing = false;
      if (!this.closed) {
        this.timer = setTimeout(() => this.run(), this.interval);
        this.timer.unref?.();
      }
    }
  }
  interrupt() {
    this.pointers.clear();
    this.pending = null;
    this.gestureId = null;
  }
  close() {
    this.closed = true;
    clearTimeout(this.timer);
    this.timer = null;
    this.interrupt();
  }
}
module.exports = { TouchTreeCoordinator };
