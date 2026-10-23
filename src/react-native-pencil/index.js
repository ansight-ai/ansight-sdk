const { NativeModules, Platform } = require("react-native");
const { createPencilRuntime } = require('@ansight/pencil-runtime');
const runtime = createPencilRuntime({
  checkPlayback: () => nativeModule().requirePlayback(),
  capture: (sample, geometry, pointerId) => nativeModule().capture(JSON.stringify({sample, geometry, pointerId})),
});
let frameworkRegistration;

function nativeModule() {
  const module = NativeModules.AnsightReactNativePencil;
  if (!module) {
    throw new Error("Ansight Pencil is not linked. Install @ansight/react-native-pencil, run pod install and rebuild the iOS app.");
  }
  return module;
}

const AnsightPencil = {
  id: 'ansight.pencil',
  async initialize() {
    if (await AnsightPencil.register() && !frameworkRegistration) {
      const registration = require('@ansight/react-native').registerTool(runtime.definition, args => runtime.handle(args));
      try { await registration.ready; frameworkRegistration = registration; }
      catch (error) { await registration.unregister(); throw error; }
    }
  },
  async isSupported() {
    return Platform.OS === "ios" ? nativeModule().isSupported() : false;
  },
  async register() {
    return Platform.OS === "ios" ? nativeModule().register() : false;
  },
};
function usePencilSurface(id, ref, handlers) {
  return require('./surface').usePencilSurface(runtime, id, ref, handlers);
}
module.exports = { AnsightPencil, usePencilSurface };
