import type {
  AnsightChannel,
  AnsightDomToolsOptions,
  AnsightErrorCaptureOptions,
  AnsightOptions,
  AnsightOptionsBuilderApi,
  AnsightRemoteToolsOptions,
  AnsightSessionJpegCaptureOptions,
  AnsightTouchCaptureOptions,
} from "./definitions";

function cloneOptions(options: AnsightOptions): AnsightOptions {
  return JSON.parse(JSON.stringify(options)) as AnsightOptions;
}

export class AnsightOptionsBuilder implements AnsightOptionsBuilderApi {
  private options: AnsightOptions;

  constructor(options: AnsightOptions = {}) {
    this.options = cloneOptions(options);
  }

  withAnsightDefaults(): this {
    this.options = {
      ...this.options,
      useNativeAllInOneDefaults: true,
      sampleFrequencyMilliseconds: 400,
      retentionPeriodSeconds: 120,
      enableFramesPerSecond: true,
      enableBatteryLevel: false,
      sessionJpegCapture: {
        intervalMilliseconds: 2000,
        quality: 60,
        maxWidth: 480,
      },
      touchCapture: {},
      toolGuard: "readOnly",
      hostAutoProbe: {
        ...this.options.hostAutoProbe,
        enabled: true,
      },
    };
    return this;
  }

  withNativeAllInOneDefaults(): this {
    return this.withAnsightDefaults();
  }

  withAnsightSdk(configure?: (builder: this) => void): this {
    this.withAnsightDefaults().withAllToolAccess();
    configure?.(this);
    return this;
  }

  withSampleFrequencyMilliseconds(value: number): this {
    this.options.sampleFrequencyMilliseconds = value;
    return this;
  }

  withFramesPerSecond(): this {
    this.options.enableFramesPerSecond = true;
    return this;
  }

  withoutFramesPerSecond(): this {
    this.options.enableFramesPerSecond = false;
    return this;
  }

  withBatteryLevel(): this {
    this.options.enableBatteryLevel = true;
    return this;
  }

  withoutBatteryLevel(): this {
    this.options.enableBatteryLevel = false;
    return this;
  }

  withRetentionPeriodSeconds(value: number): this {
    this.options.retentionPeriodSeconds = value;
    return this;
  }

  withAdditionalChannels(channels: AnsightChannel[]): this {
    this.options.additionalChannels = channels.map((channel) => ({
      ...channel,
    }));
    return this;
  }

  addAdditionalChannel(channel: AnsightChannel): this {
    this.options.additionalChannels = [
      ...(this.options.additionalChannels ?? []),
      { ...channel },
    ];
    return this;
  }

  withDefaultMemoryChannels(
    channels: NonNullable<AnsightOptions["defaultMemoryChannels"]>,
  ): this {
    this.options.defaultMemoryChannels = { ...channels };
    return this;
  }

  withoutDefaultMemoryChannels(
    channels: NonNullable<AnsightOptions["defaultMemoryChannels"]>,
  ): this {
    const current = {
      managedHeap: true,
      javaHeap: true,
      nativeHeap: true,
      residentSetSize: true,
      rss: true,
      physicalFootprint: true,
      ...this.options.defaultMemoryChannels,
    };
    for (const [key, disabled] of Object.entries(channels)) {
      if (disabled) current[key as keyof typeof current] = false;
    }
    this.options.defaultMemoryChannels = current;
    return this;
  }

  withSessionJpegCapture(options?: AnsightSessionJpegCaptureOptions): this;
  withSessionJpegCapture(
    intervalMilliseconds: number,
    quality?: number,
    maxWidth?: number | null,
    captureGpuBackedSurfaces?: boolean,
  ): this;
  withSessionJpegCapture(
    optionsOrIntervalMilliseconds:
      AnsightSessionJpegCaptureOptions | number = {},
    quality = 60,
    maxWidth: number | null = 480,
    captureGpuBackedSurfaces = true,
  ): this {
    this.options.sessionJpegCapture =
      typeof optionsOrIntervalMilliseconds === "number"
        ? {
            intervalMilliseconds: optionsOrIntervalMilliseconds,
            quality,
            maxWidth,
            captureGpuBackedSurfaces,
          }
        : {
            intervalMilliseconds: 2000,
            quality: 60,
            maxWidth: 480,
            ...optionsOrIntervalMilliseconds,
          };
    return this;
  }

  withoutSessionJpegCapture(): this {
    this.options.sessionJpegCapture = false;
    return this;
  }

  withTouchCapture(options: AnsightTouchCaptureOptions = {}): this {
    this.options.touchCapture = { ...options };
    return this;
  }

  withoutTouchCapture(): this {
    this.options.touchCapture = false;
    return this;
  }

  withLifecycleCapture(
    options: NonNullable<AnsightOptions["lifecycleCapture"]> = {},
  ): this {
    this.options.lifecycleCapture = {
      ...options,
      enabled: options.enabled ?? true,
    };
    return this;
  }

  withToolGuard(toolGuard: NonNullable<AnsightOptions["toolGuard"]>): this {
    this.options.toolGuard = toolGuard;
    return this;
  }

  withToolsDisabled(): this {
    return this.withToolGuard("disabled");
  }

  withReadOnlyToolAccess(): this {
    return this.withToolGuard("readOnly");
  }

  withReadWriteToolAccess(): this {
    return this.withToolGuard("readWrite");
  }

  withAllToolAccess(): this {
    return this.withToolGuard("fullAccess");
  }

  withHostAutoProbe(
    options: NonNullable<AnsightOptions["hostAutoProbe"]> = {},
  ): this {
    this.options.hostAutoProbe = {
      ...options,
      enabled: options.enabled ?? true,
    };
    return this;
  }

  withoutHostAutoProbe(): this {
    this.options.hostAutoProbe = {
      ...this.options.hostAutoProbe,
      enabled: false,
    };
    return this;
  }

  withHostConnection(
    options: NonNullable<AnsightOptions["hostConnection"]> = {},
  ): this {
    this.options.hostConnection = { ...options };
    return this;
  }

  configureHostConnection(
    configure: (
      options: NonNullable<AnsightOptions["hostConnection"]>,
    ) => NonNullable<AnsightOptions["hostConnection"]> | void,
  ): this {
    const current = { ...this.options.hostConnection };
    this.options.hostConnection = configure(current) ?? current;
    return this;
  }

  withBundledHostConnection(
    options: {
      bundledDeveloperConfigJson?: string;
      bundledConfigJson?: string;
    } = {},
  ): this {
    return this.configureHostConnection((current) => ({
      ...current,
      bundledDeveloperConfigJson: options.bundledDeveloperConfigJson,
      bundledConfigJson: options.bundledConfigJson,
    }));
  }

  withHostConnectionDiscoveryPort(discoveryPort: number): this {
    return this.configureHostConnection((current) => ({
      ...current,
      discoveryPort,
    }));
  }

  withCellularHostConnections(allow = true): this {
    return this.configureHostConnection((current) => ({
      ...current,
      allowCellularConnections: allow,
    }));
  }

  withHostConnectionProfileRetentionSeconds(
    connectionProfileRetentionSeconds: number,
  ): this {
    return this.configureHostConnection((current) => ({
      ...current,
      connectionProfileRetentionSeconds,
    }));
  }

  withSecureStorage(
    options: NonNullable<AnsightOptions["secureStorage"]> = {},
  ): this {
    this.options.secureStorage = {
      ...options,
      allowedKeys: options.allowedKeys ? [...options.allowedKeys] : undefined,
      allowedPrefixes: options.allowedPrefixes
        ? [...options.allowedPrefixes]
        : undefined,
    };
    return this;
  }

  withRemoteTools(options: AnsightRemoteToolsOptions = {}): this {
    this.options.remoteTools = cloneOptions({
      remoteTools: options,
    }).remoteTools;
    return this;
  }

  withVisualTreeTools(options: boolean | { enabled?: boolean } = {}): this {
    this.options.remoteTools = {
      ...this.options.remoteTools,
      visualTree:
        typeof options === "boolean"
          ? options
          : { ...options, enabled: options.enabled ?? true },
    };
    return this;
  }

  withoutVisualTreeTools(): this {
    this.options.remoteTools = {
      ...this.options.remoteTools,
      visualTree: false,
    };
    return this;
  }

  withFileSystemTools(
    options: NonNullable<AnsightRemoteToolsOptions["fileSystem"]> = {},
  ): this {
    this.options.remoteTools = {
      ...this.options.remoteTools,
      fileSystem: cloneOptions({ remoteTools: { fileSystem: options } })
        .remoteTools?.fileSystem,
    };
    return this;
  }

  withDatabaseTools(
    options: NonNullable<AnsightRemoteToolsOptions["database"]> = {},
  ): this {
    this.options.remoteTools = {
      ...this.options.remoteTools,
      database: cloneOptions({ remoteTools: { database: options } }).remoteTools
        ?.database,
    };
    return this;
  }

  withPreferencesTools(
    options: NonNullable<AnsightRemoteToolsOptions["preferences"]> = {},
  ): this {
    this.options.remoteTools = {
      ...this.options.remoteTools,
      preferences: cloneOptions({ remoteTools: { preferences: options } })
        .remoteTools?.preferences,
    };
    return this;
  }

  withReflectionTools(
    options: NonNullable<AnsightRemoteToolsOptions["reflection"]> = {},
  ): this {
    this.options.remoteTools = {
      ...this.options.remoteTools,
      reflection: cloneOptions({ remoteTools: { reflection: options } })
        .remoteTools?.reflection,
    };
    return this;
  }

  withDomTools(options: AnsightDomToolsOptions = {}): this {
    this.options.domTools = { ...options };
    return this;
  }

  withErrorCapture(options: AnsightErrorCaptureOptions = {}): this {
    this.options.errorCapture = { ...options };
    return this;
  }

  registerCustomProperty(group: string, key: string, value: unknown): this {
    this.options.customProperties = {
      ...this.options.customProperties,
      [group]: {
        ...this.options.customProperties?.[group],
        [key]: String(value),
      },
    };
    return this;
  }

  removeCustomProperty(group: string, key: string): this {
    const groups =
      cloneOptions({ customProperties: this.options.customProperties })
        .customProperties ?? {};
    delete groups[group]?.[key];
    if (groups[group] && Object.keys(groups[group]).length === 0) {
      delete groups[group];
    }
    this.options.customProperties = groups;
    return this;
  }

  clearCustomProperties(): this {
    this.options.customProperties = {};
    return this;
  }

  build(): AnsightOptions {
    return cloneOptions(this.options);
  }
}

export function createOptionsBuilder(
  options: AnsightOptions = {},
): AnsightOptionsBuilder {
  return new AnsightOptionsBuilder(options);
}
