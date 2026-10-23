import { Capacitor, registerPlugin } from '@capacitor/core';
import { registerTool } from '@ansight/capacitor';
import { createPencilRuntime, pointerProperties } from '@ansight/pencil-runtime';
import type { PenSample } from '@ansight/pencil-runtime';
declare global {
  interface PointerEvent { readonly ansightPencil?: Readonly<PenSample & {inputSource: string}>; }
}

interface NativePencil {
  isSupported(): Promise<{ value: boolean }>;
  register(): Promise<{ value: boolean }>;
  requirePlayback(): Promise<void>;
  capture(options: {json: string}): Promise<void>;
  hitTest(options: {x: number; y: number; width: number; height: number}): Promise<{value: boolean}>;
}

const nativePencil = registerPlugin<NativePencil>('AnsightPencil');
const runtime = createPencilRuntime({
  checkPlayback: () => nativePencil.requirePlayback(),
  capture: (sample, geometry, pointerId) => nativePencil.capture({ json: JSON.stringify({sample, geometry, pointerId}) }),
});
let frameworkRegistration: ReturnType<typeof registerTool> | undefined;

export const AnsightPencil = {
  id: 'ansight.pencil',
  async initialize() {
    if (await AnsightPencil.register() && !frameworkRegistration) {
      const registration = registerTool(runtime.definition, args => runtime.handle({command: args.command}));
      try { await registration.ready; frameworkRegistration = registration; }
      catch (error) { await registration.unregister(); throw error; }
    }
  },
  /** Bind the actual DOM element. Dispose before replacing it or leaving the screen. */
  bindSurface(id: string, element: HTMLElement): () => Promise<void> {
    if (!element || element.ownerDocument !== document) throw new Error('Select a DOM element in the current document.');
    const realPointers = new Set<number>();
    const simulated = new WeakSet<Event>();
    let pointer: number | undefined;
    let captured = false;
    const restores: (() => void)[] = [];
    function restoreCapture() { restores.splice(0).forEach(restore => restore()); pointer = undefined; captured = false; }
    function installCapture(pointerId: number) {
      if (pointer !== undefined) return;
      pointer = pointerId;
      // Only this explicitly bound element is adapted; native pointers keep the browser implementation.
      for (const key of ['setPointerCapture', 'releasePointerCapture', 'hasPointerCapture'] as const) {
        const descriptor = Object.getOwnPropertyDescriptor(element, key);
        const original = element[key].bind(element);
        const replacement = (id: number) => {
          if (id !== pointer) return original(id);
          if (key === 'hasPointerCapture') return captured;
          captured = key === 'setPointerCapture';
        };
        Object.defineProperty(element, key, { configurable: true, value: replacement });
        restores.push(() => {
          if (element[key] !== replacement) return;
          if (descriptor) Object.defineProperty(element, key, descriptor);
          else delete (element as unknown as Record<string, unknown>)[key];
        });
      }
    }
    const realDown = (event: PointerEvent) => { if (!simulated.has(event) && element.contains(event.target as Node)) realPointers.add(event.pointerId); };
    const realUp = (event: PointerEvent) => { if (!simulated.has(event)) realPointers.delete(event.pointerId); };
    const events = { down: 'pointerdown', move: 'pointermove', up: 'pointerup', cancel: 'pointercancel', hoverEnter: 'pointerenter', hoverMove: 'pointermove', hoverExit: 'pointerleave' };
    const dispose = runtime.bindSurface(id, {
      snapshot() {
        if (!element.isConnected || document.visibilityState === 'hidden' || (window.visualViewport?.scale ?? 1) !== 1) throw new Error('Pencil surface is detached, hidden or zoomed.');
        const rect = element.getBoundingClientRect();
        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height,
          viewportWidth: window.innerWidth, viewportHeight: window.innerHeight, scale: window.devicePixelRatio };
      },
      isBusy: () => realPointers.size > 0,
      hitTest: async point => {
        const hit = document.elementFromPoint(point.x, point.y);
        return (hit === element || (hit !== null && element.contains(hit))) &&
          (await nativePencil.hitTest({x: point.x, y: point.y, width: window.innerWidth, height: window.innerHeight})).value;
      },
      dispatch(sample, rect, pointerId) {
        if (sample.phase === 'down') installCapture(pointerId);
        const properties = pointerProperties(sample, rect, pointerId);
        const event = new PointerEvent(events[sample.phase], { ...properties,
          bubbles: !['hoverEnter', 'hoverExit'].includes(sample.phase), cancelable: true, composed: true });
        Object.defineProperty(event, 'ansightPencil', { value: properties.ansightPencil });
        simulated.add(event);
        element.dispatchEvent(event);
        if (sample.phase === 'up' || sample.phase === 'cancel') restoreCapture();
      },
      finish: restoreCapture,
      onError: error => console.error('Ansight Pencil cleanup failed', error),
    });
    window.addEventListener('pointerdown', realDown, true);
    window.addEventListener('pointerup', realUp, true);
    window.addEventListener('pointercancel', realUp, true);
    return async () => {
      window.removeEventListener('pointerdown', realDown, true);
      window.removeEventListener('pointerup', realUp, true);
      window.removeEventListener('pointercancel', realUp, true);
      await dispose();
    };
  },
  /** Platform capability only. Does not register tools or install hooks. */
  async isSupported(): Promise<boolean> {
    return Capacitor.getPlatform() === 'ios' && (await nativePencil.isSupported()).value;
  },
  /** Low-level registration. Prefer withPlugin(AnsightPencil) in the options builder.
   * Returns false outside iOS Simulator; rejects before initialization.
   * Idempotent. Grants only Pencil tools; global disable still wins. Does not install hooks. */
  async register(): Promise<boolean> {
    return Capacitor.getPlatform() === 'ios' && (await nativePencil.register()).value;
  },
};
