import type { RefObject } from 'react';
import type { View, ViewProps, NativeSyntheticEvent, PointerEvent } from 'react-native';
import type { PenSample } from '@ansight/pencil-runtime';
export type AnsightPencilEvent = NativeSyntheticEvent<PointerEvent & {
  ansightPencil?: PenSample & {inputSource: string};
}>;
export declare const AnsightPencil: {
  readonly id: "ansight.pencil";
  /** Used by createOptionsBuilder().withPlugin(AnsightPencil). */
  initialize(): Promise<void>;
  /** Platform capability only. Does not register tools or install hooks. */
  isSupported(): Promise<boolean>;
  /** Low-level registration. Prefer withPlugin(AnsightPencil) in the options builder.
   * Returns false outside iOS Simulator; rejects before initialization.
   * Idempotent. Grants only Pencil tools; global disable still wins. Does not install hooks. */
  register(): Promise<boolean>;
};
/** Spread the result on the same View as ref. Keeps real handlers and supplies scoped SDK playback. */
export declare function usePencilSurface(id: string, ref: RefObject<View | null>, handlers: ViewProps): ViewProps;
