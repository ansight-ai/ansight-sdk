import type { TaskDefinition, TaskInvocation } from "./ansight-task.d.ts";

export const task = {
  "schemaVersion": 1,
  "title": "Validate Avalonia session metadata",
  "description": "Assert captured Avalonia and .NET metadata for a benchmark child app.",
  "maximumActions": 2
} satisfies TaskDefinition;

type Metadata = {
  avalonia?: Record<string, unknown>;
  dotnet?: Record<string, unknown>;
};

export default async function runTask({ ansight, expect }: TaskInvocation) {
  const result = await ansight.session.getProperties<Metadata>();
  const avalonia = result.customProperties.avalonia ?? {};
  const dotnet = result.customProperties.dotnet ?? {};

  expect(avalonia.framework, { id: "avalonia-framework" }).toBe("Avalonia");
  for (const key of ["version", "sdkVersion", "applicationType", "applicationLifetime", "renderingSubsystem", "windowingSubsystem"]) {
    const value = avalonia[key];
    expect(typeof value === "string" && value.trim().length > 0 && value !== "unknown", {
      id: `avalonia-${key}`,
      message: `The retained session must contain an observed Avalonia ${key}.`
    }).toBe(true);
  }

  expect(typeof dotnet.runtimeVersion === "string" && dotnet.runtimeVersion.length > 0, {
    id: "dotnet-runtime-version",
    message: "Avalonia metadata must coexist with the underlying .NET runtime metadata."
  }).toBe(true);

  return { avalonia, dotnetRuntimeVersion: dotnet.runtimeVersion };
}
