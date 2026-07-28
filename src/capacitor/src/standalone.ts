import Ansight from "./index";
import type { AnsightOptions } from "./definitions";

declare global {
  var __ANSIGHT_CAPACITOR_STANDALONE_OPTIONS__: AnsightOptions | undefined;
}

if (typeof window !== "undefined") {
  const options = Ansight.createOptionsBuilder(
    globalThis.__ANSIGHT_CAPACITOR_STANDALONE_OPTIONS__ ?? {},
  )
    .withAnsightDefaults()
    .withAllToolAccess()
    .withVisualTreeTools()
    .withFileSystemTools()
    .withDatabaseTools()
    .withPreferencesTools()
    .withReflectionTools()
    .withDomTools()
    .withErrorCapture()
    .build();

  void Ansight.initializeAndActivate(options).catch((error) => {
    console.error("[Ansight Capacitor]", error);
  });
}
