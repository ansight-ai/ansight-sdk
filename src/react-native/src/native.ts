import { NativeModules } from "react-native";

import type {
  AnsightDebugSnapshot,
  AnsightOptions,
  AnsightToolDescriptor,
  EventOptions,
  OpenSessionResult,
  PairingOpenOptions,
} from "./types";

type NativeAnsightModule = {
  initialize(options?: AnsightOptions): Promise<void>;
  activate(): Promise<void>;
  deactivate(): Promise<void>;
  clear(): Promise<void>;
  metric(value: string | number, channel?: number): Promise<void>;
  event(label: string, options?: EventOptions): Promise<void>;
  openSession(pairingJson: string, options: PairingOpenOptions): Promise<OpenSessionResult>;
  completeSession(): Promise<void>;
  closeSession(): Promise<void>;
  registerTool(tool: AnsightToolDescriptor): Promise<void>;
  getDebugSnapshot(): Promise<AnsightDebugSnapshot>;
};

const nativeModule = NativeModules.AnsightBridgeModule as NativeAnsightModule | undefined;

function requireNativeModule(): NativeAnsightModule {
  if (!nativeModule) {
    throw new Error("AnsightBridgeModule is not linked.");
  }

  return nativeModule;
}

export const NativeAnsight = {
  initialize(options?: AnsightOptions) {
    return requireNativeModule().initialize(options);
  },
  activate() {
    return requireNativeModule().activate();
  },
  deactivate() {
    return requireNativeModule().deactivate();
  },
  clear() {
    return requireNativeModule().clear();
  },
  metric(value: string | number, channel?: number) {
    return requireNativeModule().metric(String(value), channel);
  },
  event(label: string, options?: EventOptions) {
    return requireNativeModule().event(label, options);
  },
  openSession(pairingJson: string, options: PairingOpenOptions) {
    return requireNativeModule().openSession(pairingJson, options);
  },
  completeSession() {
    return requireNativeModule().completeSession();
  },
  closeSession() {
    return requireNativeModule().closeSession();
  },
  registerTool(tool: AnsightToolDescriptor) {
    return requireNativeModule().registerTool(tool);
  },
  getDebugSnapshot() {
    return requireNativeModule().getDebugSnapshot();
  },
};
