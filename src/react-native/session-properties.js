"use strict";

const { version: sdkVersion } = require("./package.json");

const REACT_NATIVE_GROUP = "reactNative";
const LOCALIZATION_GROUP = "localization";

function formatBoolean(value) {
  return value ? "true" : "false";
}

function normalizeString(value) {
  if (value == null) {
    return undefined;
  }
  const normalized = String(value).trim();
  return normalized || undefined;
}

function formatReactNativeVersion(version) {
  if (!version || typeof version !== "object") {
    return undefined;
  }
  const major = normalizeString(version.major);
  const minor = normalizeString(version.minor);
  const patch = normalizeString(version.patch);
  if (major == null || minor == null || patch == null) {
    return undefined;
  }
  const prerelease = normalizeString(version.prerelease);
  return `${major}.${minor}.${patch}${prerelease ? `-${prerelease}` : ""}`;
}

function resolveJavaScriptEngine(runtimeGlobal) {
  if (runtimeGlobal && runtimeGlobal.HermesInternal) {
    return "hermes";
  }
  if (runtimeGlobal && runtimeGlobal._v8runtime) {
    return "v8";
  }
  return "javascriptCore";
}

function readHermesProperties(runtimeGlobal) {
  try {
    return runtimeGlobal?.HermesInternal?.getRuntimeProperties?.() || {};
  } catch (_) {
    return {};
  }
}

function canonicalizeLocale(value) {
  const normalized = normalizeString(value)?.replace(/_/g, "-");
  if (!normalized) {
    return undefined;
  }
  try {
    return Intl.getCanonicalLocales(normalized)[0] || normalized;
  } catch (_) {
    return normalized;
  }
}

function parseLocale(locale) {
  const parts = (locale || "").split("-").filter(Boolean);
  const language = parts[0]?.toLowerCase();
  const region = parts.find(
    (part, index) => index > 0 && (/^[A-Za-z]{2}$/.test(part) || /^\d{3}$/.test(part))
  );
  return {
    language,
    region: region ? region.toUpperCase() : undefined,
  };
}

function createLocalizationProperties() {
  let resolved = {};
  try {
    resolved = Intl.DateTimeFormat().resolvedOptions();
  } catch (_) {
    // Older JavaScript runtimes may not provide Intl data.
  }

  const locale = canonicalizeLocale(resolved.locale);
  const parsed = parseLocale(locale);
  const properties = {
    utcOffsetMinutes: String(-new Date().getTimezoneOffset()),
  };
  if (locale) properties.locale = locale;
  if (parsed.language) properties.language = parsed.language;
  if (parsed.region) properties.region = parsed.region;
  if (normalizeString(resolved.timeZone)) properties.timeZone = resolved.timeZone;
  return properties;
}

function createAutomaticSessionProperties({
  platform,
  reactVersion,
  runtimeGlobal,
  developmentMode,
} = {}) {
  const reactNativeVersion = formatReactNativeVersion(platform?.constants?.reactNativeVersion);
  const newArchitectureEnabled = Boolean(
    runtimeGlobal?.nativeFabricUIManager || runtimeGlobal?.__turboModuleProxy
  );
  const hermesProperties = readHermesProperties(runtimeGlobal);
  const engineVersion = normalizeString(
    hermesProperties["OSS Release Version"] || hermesProperties["Release Version"]
  );
  const bytecodeVersion = normalizeString(hermesProperties["Bytecode Version"]);

  const properties = {
    sdkVersion,
    platform: normalizeString(platform?.OS) || "unknown",
    runtimeLanguage: "javascript",
    javascriptEngine: resolveJavaScriptEngine(runtimeGlobal),
    architecture: newArchitectureEnabled ? "new" : "legacy",
    newArchitectureEnabled: formatBoolean(newArchitectureEnabled),
    bridgelessEnabled: formatBoolean(Boolean(runtimeGlobal?.RN$Bridgeless)),
    developmentMode: formatBoolean(Boolean(developmentMode)),
  };
  if (reactNativeVersion) properties.reactNativeVersion = reactNativeVersion;
  if (normalizeString(reactVersion)) properties.reactVersion = String(reactVersion).trim();
  if (engineVersion) properties.javascriptEngineVersion = engineVersion;
  if (bytecodeVersion) properties.hermesBytecodeVersion = bytecodeVersion;

  return {
    [REACT_NATIVE_GROUP]: properties,
    [LOCALIZATION_GROUP]: createLocalizationProperties(),
  };
}

function mergeSessionProperties(automaticProperties, customProperties) {
  const merged = {};
  for (const [group, properties] of Object.entries(automaticProperties || {})) {
    merged[group] = { ...(properties || {}) };
  }
  for (const [group, properties] of Object.entries(customProperties || {})) {
    merged[group] = { ...(merged[group] || {}), ...(properties || {}) };
  }
  return merged;
}

module.exports = {
  LOCALIZATION_GROUP,
  REACT_NATIVE_GROUP,
  createAutomaticSessionProperties,
  createLocalizationProperties,
  formatReactNativeVersion,
  mergeSessionProperties,
};
