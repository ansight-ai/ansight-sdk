import type { AnsightOptions } from "./definitions";
import { createOptionsBuilder } from "./options";

export function createStandaloneOptions(
  overrides: AnsightOptions = {},
): AnsightOptions {
  return createOptionsBuilder(overrides)
    .withAnsightDefaults()
    .withVisualTreeTools()
    .withFileSystemTools()
    .withDatabaseTools()
    .withPreferencesTools()
    .withReflectionTools()
    .withDomTools()
    .withErrorCapture()
    .withToolGuard(overrides.toolGuard ?? "readOnly")
    .build();
}
