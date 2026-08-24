import Ansight from "./index";
import type { AnsightOptions } from "./definitions";
import { createStandaloneOptions } from "./standalone-options";

declare global {
  var __ANSIGHT_CAPACITOR_STANDALONE_OPTIONS__: AnsightOptions | undefined;
}

if (typeof window !== "undefined") {
  const options = createStandaloneOptions(
    globalThis.__ANSIGHT_CAPACITOR_STANDALONE_OPTIONS__ ?? {},
  );

  void Ansight.initializeAndActivate(options).catch((error) => {
    console.error("[Ansight Capacitor]", error);
  });
}
