export interface PenSample { phase: 'down'|'move'|'up'|'cancel'|'hoverEnter'|'hoverMove'|'hoverExit'; x: number; y: number; pressure: number; altitudeRadians: number; azimuthRadians: number; rollRadians: number; distance: number; }
export interface SurfaceGeometry { x: number; y: number; width: number; height: number; viewportWidth: number; viewportHeight: number; scale: number; }
export interface PencilAdapter {
  validate?(samples: PenSample[]): void;
  snapshot(): SurfaceGeometry | Promise<SurfaceGeometry>;
  hitTest(point: {x: number; y: number}): boolean | Promise<boolean>;
  dispatch(sample: PenSample, geometry: SurfaceGeometry, pointerId: number): void | Promise<void>;
  finish?(pointerId: number): void | Promise<void>;
  isBusy?(): boolean;
  onError?(error: unknown): void;
}
export function createPencilRuntime(options: {
  capture(sample: PenSample, geometry: SurfaceGeometry, pointerId: number): Promise<void>;
  checkPlayback(): Promise<void>; timeoutMs?: number;
}): {
  definition: { id: string; name: string; category: string; policy: 'write'; description: string; argumentsSchema: Record<string, unknown> };
  bindSurface(id: string, adapter: PencilAdapter): () => Promise<void>;
  handle(args: {command: string}): Promise<{success: true; result?: unknown} | {success: false; message: string; errorCode?: string}>;
};
export function pointerProperties(sample: PenSample, geometry: SurfaceGeometry, pointerId: number): {
  pointerId: number; pointerType: string; isPrimary: boolean; clientX: number; clientY: number;
  pageX: number; pageY: number; pressure: number; width: number; height: number;
  buttons: number; button: number; tiltX: number; tiltY: number; twist: number;
  altitudeAngle: number; azimuthAngle: number; ansightPencil: PenSample & {inputSource: string};
};
export function validateSequence(samples: unknown[]): PenSample[];
