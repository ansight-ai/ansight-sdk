export const ANSIGHT_CAPACITOR_SDK_VERSION = "1.4.0-preview.5";
export const COMPILED_CAPACITOR_CORE_VERSION = "8.4.2";

export const CAPACITOR_GROUP = "capacitor";
export const LOCALIZATION_GROUP = "localization";

export interface CapacitorSessionEnvironment {
  platform: string;
  nativePlatform: boolean;
  userAgent?: string;
  locale?: string;
  timeZone?: string;
  utcOffsetMinutes?: number;
}

function normalized(value: unknown): string | undefined {
  if (value == null) return undefined;
  const result = String(value).trim();
  return result || undefined;
}

function canonicalizeLocale(value: unknown): string | undefined {
  const locale = normalized(value)?.replace(/_/g, "-");
  if (!locale) return undefined;
  try {
    return Intl.getCanonicalLocales(locale)[0] ?? locale;
  } catch {
    return locale;
  }
}

function parseLocale(locale: string | undefined): {
  language?: string;
  region?: string;
} {
  const parts = (locale ?? "").split("-").filter(Boolean);
  const language = parts[0]?.toLowerCase();
  const region = parts.find(
    (part, index) =>
      index > 0 && (/^[A-Za-z]{2}$/.test(part) || /^\d{3}$/.test(part)),
  );
  return { language, region: region?.toUpperCase() };
}

function webViewDetails(
  platform: string,
  nativePlatform: boolean,
  userAgent: string | undefined,
): { engine: string; version?: string } {
  const agent = userAgent ?? "";
  if (nativePlatform && platform === "ios") {
    return {
      engine: "wkWebView",
      version: /AppleWebKit\/([^\s]+)/.exec(agent)?.[1],
    };
  }
  if (nativePlatform && platform === "android") {
    return {
      engine: "chromiumWebView",
      version: /(?:Chrome|Chromium)\/([^\s]+)/.exec(agent)?.[1],
    };
  }
  if (/Firefox\/([^\s]+)/.test(agent)) {
    return { engine: "gecko", version: /Firefox\/([^\s]+)/.exec(agent)?.[1] };
  }
  if (/(?:Chrome|Chromium)\/([^\s]+)/.test(agent)) {
    return {
      engine: "chromium",
      version: /(?:Chrome|Chromium)\/([^\s]+)/.exec(agent)?.[1],
    };
  }
  if (/AppleWebKit\/([^\s]+)/.test(agent)) {
    return {
      engine: "webkit",
      version: /AppleWebKit\/([^\s]+)/.exec(agent)?.[1],
    };
  }
  return { engine: "unknown" };
}

export function currentCapacitorSessionEnvironment(
  platform: string,
  nativePlatform: boolean,
): CapacitorSessionEnvironment {
  let resolved: Intl.ResolvedDateTimeFormatOptions | undefined;
  try {
    resolved = Intl.DateTimeFormat().resolvedOptions();
  } catch {
    resolved = undefined;
  }
  return {
    platform,
    nativePlatform,
    userAgent:
      typeof navigator === "undefined" ? undefined : navigator.userAgent,
    locale:
      resolved?.locale ??
      (typeof navigator === "undefined" ? undefined : navigator.language),
    timeZone: resolved?.timeZone,
    utcOffsetMinutes: -new Date().getTimezoneOffset(),
  };
}

export function createAutomaticSessionProperties(
  environment: CapacitorSessionEnvironment,
): Record<string, Record<string, string>> {
  const platform = normalized(environment.platform) ?? "unknown";
  const userAgent = normalized(environment.userAgent);
  const webView = webViewDetails(
    platform,
    environment.nativePlatform,
    userAgent,
  );
  const locale = canonicalizeLocale(environment.locale);
  const parsedLocale = parseLocale(locale);

  const capacitor: Record<string, string> = {
    sdkVersion: ANSIGHT_CAPACITOR_SDK_VERSION,
    capacitorVersion: "8.x",
    compiledCapacitorVersion: COMPILED_CAPACITOR_CORE_VERSION,
    platform,
    runtimeLanguage: "javascript",
    executionMode: environment.nativePlatform ? "native" : "web",
    webViewEngine: webView.engine,
  };
  if (webView.version) capacitor.webViewEngineVersion = webView.version;
  if (userAgent) capacitor.userAgent = userAgent;

  const localization: Record<string, string> = {
    utcOffsetMinutes: String(
      environment.utcOffsetMinutes ?? -new Date().getTimezoneOffset(),
    ),
  };
  if (locale) localization.locale = locale;
  if (parsedLocale.language) localization.language = parsedLocale.language;
  if (parsedLocale.region) localization.region = parsedLocale.region;
  if (normalized(environment.timeZone)) {
    localization.timeZone = String(environment.timeZone).trim();
  }

  return {
    [CAPACITOR_GROUP]: capacitor,
    [LOCALIZATION_GROUP]: localization,
  };
}

export function mergeSessionProperties(
  automaticProperties: Record<string, Record<string, string>>,
  customProperties?: Record<string, Record<string, string>>,
): Record<string, Record<string, string>> {
  const merged = Object.fromEntries(
    Object.entries(automaticProperties).map(([group, properties]) => [
      group,
      { ...properties },
    ]),
  );
  for (const [group, properties] of Object.entries(customProperties ?? {})) {
    merged[group] = { ...(merged[group] ?? {}), ...properties };
  }
  return merged;
}
