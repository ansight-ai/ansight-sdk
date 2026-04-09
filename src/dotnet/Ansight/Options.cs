namespace Ansight;

using Ansight.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Configures how Ansight samples, retains, and logs runtime data.
/// </summary>
public class Options
{
    /// <summary>
    /// Default options instance: 500ms sampling, 10-minute retention, FPS on.
    /// </summary>
    public static readonly Options Default = new Options()
    {
        SampleFrequencyMilliseconds = Constants.DefaultSampleFrequencyMilliseconds,
        RetentionPeriodSeconds = Constants.DefaultRetentionPeriodSeconds,
        AdditionalChannels = new List<Channel>(),
        DefaultMemoryChannels = DefaultMemoryChannels.PlatformDefaults,
        AdditionalLogger = new ConsoleLogger(),
        EnableFramesPerSecond = true,
        Tools = ToolRegistry.Empty,
        ToolGuard = ToolGuard.Disabled,
        HostAutoProbe = HostAutoProbeOptions.EnabledDefault.Clone(),
        HostConnection = HostConnectionOptions.Default.Clone()
    };

    /// <summary>
    /// Sampling cadence in milliseconds.
    /// </summary>
    public ushort SampleFrequencyMilliseconds { get; private set; } = Constants.DefaultSampleFrequencyMilliseconds;

    /// <summary>
    /// How long metric/event samples are retained before trim, in seconds.
    /// </summary>
    public ushort RetentionPeriodSeconds { get; private set; } = Constants.DefaultRetentionPeriodSeconds;

    /// <summary>
    /// Maximum buffered samples calculated from retention and frequency.
    /// </summary>
    public int MaximumBufferSize => RetentionPeriodSeconds * (int)Math.Ceiling(1000f / (float)SampleFrequencyMilliseconds);

    /// <summary>
    /// Additional metric/event channels to track besides the built-in ones.
    /// </summary>
    public List<Channel> AdditionalChannels { get; private set; } = new();

    /// <summary>
    /// Controls which of the built-in memory channels should be exposed.
    /// </summary>
    public DefaultMemoryChannels DefaultMemoryChannels { get; private set; } = DefaultMemoryChannels.PlatformDefaults;

    /// <summary>
    /// Optional additional logger to receive Ansight log messages.
    /// </summary>
    public ILogCallback? AdditionalLogger { get; private set; }

    /// <summary>
    /// Enable frames-per-second sampling at startup.
    /// </summary>
    public bool EnableFramesPerSecond { get; private set; } = false;

    /// <summary>
    /// Registered remote tools available to paired hosts.
    /// </summary>
    public ToolRegistry Tools { get; private set; } = ToolRegistry.Empty;

    /// <summary>
    /// Optional periodic JPEG capture streamed over live pairing sessions.
    /// Enabling this renders and compresses the app surface during active pairing sessions and can negatively affect runtime performance.
    /// </summary>
    public SessionJpegCaptureOptions? SessionJpegCapture { get; private set; }

    /// <summary>
    /// Guard policy controlling whether registered tools may be discovered and executed.
    /// </summary>
    public ToolGuard ToolGuard { get; private set; } = ToolGuard.Disabled;

    /// <summary>
    /// Background host auto-probe policy used while the runtime is active.
    /// </summary>
    public HostAutoProbeOptions HostAutoProbe { get; private set; } = HostAutoProbeOptions.EnabledDefault.Clone();

    /// <summary>
    /// Runtime-owned host connection configuration used to resolve saved and bundled configs.
    /// </summary>
    public HostConnectionOptions HostConnection { get; private set; } = HostConnectionOptions.Default.Clone();

    public void Validate()
    {
        if (SampleFrequencyMilliseconds > Constants.MaxSampleFrequencyMilliseconds)
        {
            Logger.Warning($"The 'SampleFrequencyMilliseconds' was above the minimum frequency of '{Constants.MaxSampleFrequencyMilliseconds}' milliseconds. The sampling rate has been coerced to '{Constants.MaxSampleFrequencyMilliseconds}'");
            SampleFrequencyMilliseconds = Constants.MaxSampleFrequencyMilliseconds;
        }

        if (SampleFrequencyMilliseconds < Constants.MinSampleFrequencyMilliseconds)
        {
            Logger.Warning($"The 'SampleFrequencyMilliseconds' was below the minimum frequency of '{Constants.MinSampleFrequencyMilliseconds}' milliseconds. The sampling rate has been coerced to '{Constants.MinSampleFrequencyMilliseconds}'");
            SampleFrequencyMilliseconds = Constants.MinSampleFrequencyMilliseconds;
        }

        if (RetentionPeriodSeconds > Constants.MaxRetentionPeriodSeconds)
        {
            Logger.Warning($"The 'RetentionPeriodSeconds' was above the maximum retention of '{Constants.MaxRetentionPeriodSeconds}' seconds. The retention range has been coerced to '{Constants.MaxRetentionPeriodSeconds}'");
            RetentionPeriodSeconds = Constants.MaxRetentionPeriodSeconds;
        }

        if (RetentionPeriodSeconds < Constants.MinRetentionPeriodSeconds)
        {
            Logger.Warning($"The 'RetentionPeriodSeconds' was below the minimum retention of '{Constants.MinRetentionPeriodSeconds}' seconds. The retention range has been coerced to '{Constants.MinRetentionPeriodSeconds}'");
            RetentionPeriodSeconds = Constants.MinRetentionPeriodSeconds;
        }

        if (AdditionalChannels != null && AdditionalChannels.Count > 0)
        {
            var usesPredefinedChannels = AdditionalChannels.Where(Constants.IsPredefinedChannel).ToList();
            if (usesPredefinedChannels.Any())
            {
                throw new InvalidOperationException("One or more additional channels use a reserved channel ID. " + string.Join(", ", usesPredefinedChannels.Select(x => x.Name + " uses reserved channel " + x.Id)));
            }
        }

        Tools = Tools ?? ToolRegistry.Empty;
        Tools.Validate();
        ToolGuard = ToolGuard ?? ToolGuard.Disabled;
        ToolGuard.Validate();
        HostAutoProbe ??= HostAutoProbeOptions.EnabledDefault.Clone();
        HostConnection ??= HostConnectionOptions.Default.Clone();

        if (HostAutoProbe.InitialDelay < TimeSpan.Zero)
        {
            Logger.Warning("The 'HostAutoProbe.InitialDelay' was negative. It has been coerced to zero.");
            HostAutoProbe.InitialDelay = TimeSpan.Zero;
        }

        if (HostAutoProbe.ProbeInterval < TimeSpan.FromSeconds(1))
        {
            Logger.Warning("The 'HostAutoProbe.ProbeInterval' was below one second. It has been coerced to one second.");
            HostAutoProbe.ProbeInterval = TimeSpan.FromSeconds(1);
        }

        if (HostAutoProbe.ReconnectDelay < TimeSpan.FromSeconds(1))
        {
            Logger.Warning("The 'HostAutoProbe.ReconnectDelay' was below one second. It has been coerced to one second.");
            HostAutoProbe.ReconnectDelay = TimeSpan.FromSeconds(1);
        }

        if (SessionJpegCapture is not null)
        {
            if (SessionJpegCapture.IntervalMilliseconds < 250)
            {
                Logger.Warning("The 'SessionJpegCapture.IntervalMilliseconds' was below 250ms. It has been coerced to 250ms.");
                SessionJpegCapture.IntervalMilliseconds = 250;
            }

            if (SessionJpegCapture.Quality < 1 || SessionJpegCapture.Quality > 100)
            {
                Logger.Warning("The 'SessionJpegCapture.Quality' was outside 1-100. It has been coerced into range.");
                SessionJpegCapture.Quality = Math.Clamp(SessionJpegCapture.Quality, 1, 100);
            }

            if (SessionJpegCapture.MaxWidth is <= 0)
            {
                Logger.Warning("The 'SessionJpegCapture.MaxWidth' was not positive. Full-size capture will be used instead.");
                SessionJpegCapture.MaxWidth = null;
            }
            else if (SessionJpegCapture.MaxWidth > 8192)
            {
                Logger.Warning("The 'SessionJpegCapture.MaxWidth' was above 8192. It has been coerced to 8192.");
                SessionJpegCapture.MaxWidth = 8192;
            }
        }
    }

    public static OptionsBuilder CreateBuilder() => new OptionsBuilder();

    /// <summary>
    /// Creates a builder seeded with the provided options instance.
    /// </summary>
    public static OptionsBuilder CreateBuilder(Options options) => new OptionsBuilder(options);

    /// <summary>
    /// Fluent builder for <see cref="Options"/>.
    /// </summary>
    public sealed class OptionsBuilder
    {
        private readonly Options options;

        public OptionsBuilder()
        {
            options = new Options();
        }

        internal OptionsBuilder(Options initialOptions)
        {
            if (initialOptions == null) throw new ArgumentNullException(nameof(initialOptions));

            options = new Options
            {
                SampleFrequencyMilliseconds = initialOptions.SampleFrequencyMilliseconds,
                RetentionPeriodSeconds = initialOptions.RetentionPeriodSeconds,
                AdditionalChannels = initialOptions.AdditionalChannels?.ToList() ?? new List<Channel>(),
                DefaultMemoryChannels = initialOptions.DefaultMemoryChannels,
                AdditionalLogger = initialOptions.AdditionalLogger,
                EnableFramesPerSecond = initialOptions.EnableFramesPerSecond,
                Tools = initialOptions.Tools ?? ToolRegistry.Empty,
                SessionJpegCapture = initialOptions.SessionJpegCapture is null
                    ? null
                    : new SessionJpegCaptureOptions
                    {
                        IntervalMilliseconds = initialOptions.SessionJpegCapture.IntervalMilliseconds,
                        Quality = initialOptions.SessionJpegCapture.Quality,
                        MaxWidth = initialOptions.SessionJpegCapture.MaxWidth
                    },
                ToolGuard = initialOptions.ToolGuard ?? ToolGuard.Disabled,
                HostAutoProbe = initialOptions.HostAutoProbe?.Clone() ?? HostAutoProbeOptions.EnabledDefault.Clone(),
                HostConnection = initialOptions.HostConnection?.Clone() ?? HostConnectionOptions.Default.Clone()
            };
        }

        /// <summary>
        /// Sets the sampling cadence in milliseconds.
        /// </summary>
        public OptionsBuilder WithSampleFrequencyMilliseconds(ushort sampleFrequencyMilliseconds)
        {
            options.SampleFrequencyMilliseconds = sampleFrequencyMilliseconds;
            return this;
        }

        /// <summary>
        /// Enables frames-per-second sampling at startup.
        /// </summary>
        public OptionsBuilder WithFramesPerSecond()
        {
            options.EnableFramesPerSecond = true;
            return this;
        }

        /// <summary>
        /// Sets the retention period, in seconds, for buffered samples.
        /// </summary>
        public OptionsBuilder WithRetentionPeriodSeconds(ushort retentionPeriodSeconds)
        {
            options.RetentionPeriodSeconds = retentionPeriodSeconds;
            return this;
        }

        /// <summary>
        /// Replaces the additional channels collection.
        /// </summary>
        public OptionsBuilder WithAdditionalChannels(IEnumerable<Channel> additionalChannels)
        {
            if (additionalChannels == null) throw new ArgumentNullException(nameof(additionalChannels));

            options.AdditionalChannels = additionalChannels.ToList();
            return this;
        }

        /// <summary>
        /// Adds a single additional channel to the collection.
        /// </summary>
        public OptionsBuilder AddAdditionalChannel(Channel additionalChannel)
        {
            if (additionalChannel == null) throw new ArgumentNullException(nameof(additionalChannel));

            options.AdditionalChannels ??= new List<Channel>();
            options.AdditionalChannels.Add(additionalChannel);
            return this;
        }

        /// <summary>
        /// Specifies which of the built-in memory channels should be tracked.
        /// </summary>
        public OptionsBuilder WithDefaultMemoryChannels(DefaultMemoryChannels memoryChannels)
        {
            options.DefaultMemoryChannels = memoryChannels;
            return this;
        }

        /// <summary>
        /// Removes the provided built-in memory channels from the configuration.
        /// </summary>
        public OptionsBuilder WithoutDefaultMemoryChannels(DefaultMemoryChannels memoryChannels)
        {
            options.DefaultMemoryChannels &= ~memoryChannels;
            return this;
        }

        /// <summary>
        /// Adds an external logger for Ansight logs.
        /// </summary>
        public OptionsBuilder WithAdditionalLogger(ILogCallback logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            options.AdditionalLogger = logger;
            return this;
        }

        /// <summary>
        /// Enables the built-in console logger.
        /// </summary>
        public OptionsBuilder WithBuiltInLogger()
        {
            options.AdditionalLogger = new ConsoleLogger();
            return this;
        }

        /// <summary>
        /// Replaces the registered tool collection.
        /// </summary>
        /// <param name="tools">Tools to register for remote discovery and execution.</param>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithTools(IEnumerable<ITool> tools)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));

            options.Tools = new ToolRegistry(tools);
            return this;
        }

        /// <summary>
        /// Adds a single tool to the registered tool collection.
        /// </summary>
        /// <param name="tool">Tool to add.</param>
        /// <returns>The current builder.</returns>
        public OptionsBuilder AddTool(ITool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));

            options.Tools = (options.Tools ?? ToolRegistry.Empty).Add(tool);
            return this;
        }

        /// <summary>
        /// Adds multiple tools to the registered tool collection.
        /// </summary>
        /// <param name="tools">Tools to add.</param>
        /// <returns>The current builder.</returns>
        public OptionsBuilder AddTools(IEnumerable<ITool> tools)
        {
            if (tools == null) throw new ArgumentNullException(nameof(tools));

            options.Tools = (options.Tools ?? ToolRegistry.Empty).AddRange(tools);
            return this;
        }

        /// <summary>
        /// Enables periodic JPEG capture while an Ansight pairing session is open.
        /// This adds extra rendering, encoding, and transport work and can negatively affect runtime performance.
        /// </summary>
        public OptionsBuilder WithSessionJpegCapture(
            ushort intervalMilliseconds = 2000,
            int quality = 70,
            int? maxWidth = 720)
        {
            options.SessionJpegCapture = new SessionJpegCaptureOptions
            {
                IntervalMilliseconds = intervalMilliseconds,
                Quality = quality,
                MaxWidth = maxWidth
            };
            return this;
        }

        /// <summary>
        /// Enables periodic JPEG capture while an Ansight pairing session is open using a fully configured options object.
        /// This adds extra rendering, encoding, and transport work and can negatively affect runtime performance.
        /// </summary>
        public OptionsBuilder WithSessionJpegCapture(SessionJpegCaptureOptions sessionJpegCapture)
        {
            if (sessionJpegCapture == null) throw new ArgumentNullException(nameof(sessionJpegCapture));

            options.SessionJpegCapture = new SessionJpegCaptureOptions
            {
                IntervalMilliseconds = sessionJpegCapture.IntervalMilliseconds,
                Quality = sessionJpegCapture.Quality,
                MaxWidth = sessionJpegCapture.MaxWidth
            };
            return this;
        }

        /// <summary>
        /// Disables periodic JPEG capture for live pairing sessions.
        /// </summary>
        public OptionsBuilder WithoutSessionJpegCapture()
        {
            options.SessionJpegCapture = null;
            return this;
        }

        /// <summary>
        /// Replaces the tool guard policy.
        /// </summary>
        /// <param name="toolGuard">Guard policy that controls tool discovery and execution.</param>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithToolGuard(ToolGuard toolGuard)
        {
            options.ToolGuard = toolGuard ?? throw new ArgumentNullException(nameof(toolGuard));
            return this;
        }

        /// <summary>
        /// Disables both tool discovery and tool execution.
        /// </summary>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithToolsDisabled()
        {
            options.ToolGuard = ToolGuard.Disabled;
            return this;
        }

        /// <summary>
        /// Enables discovery and execution for read-only tools.
        /// </summary>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithReadOnlyToolAccess()
        {
            options.ToolGuard = ToolGuard.ReadOnly;
            return this;
        }

        /// <summary>
        /// Enables discovery and execution for read and write tools.
        /// Delete-scoped tools remain disabled.
        /// </summary>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithReadWriteToolAccess()
        {
            options.ToolGuard = ToolGuard.ReadWrite;
            return this;
        }

        /// <summary>
        /// Enables discovery and execution for all registered tool scopes.
        /// </summary>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithAllToolAccess()
        {
            options.ToolGuard = ToolGuard.FullAccess;
            return this;
        }

        /// <summary>
        /// Enables host auto-probe using the provided options or the package defaults when omitted.
        /// </summary>
        public OptionsBuilder WithHostAutoProbe(HostAutoProbeOptions? hostAutoProbe = null)
        {
            options.HostAutoProbe = (hostAutoProbe ?? HostAutoProbeOptions.EnabledDefault).Clone();
            options.HostAutoProbe.Enabled = true;
            return this;
        }

        /// <summary>
        /// Disables host auto-probe.
        /// </summary>
        public OptionsBuilder WithoutHostAutoProbe()
        {
            options.HostAutoProbe = HostAutoProbeOptions.DisabledDefault.Clone();
            return this;
        }

        /// <summary>
        /// Replaces the runtime-owned host connection configuration.
        /// </summary>
        public OptionsBuilder WithHostConnection(HostConnectionOptions? hostConnection = null)
        {
            options.HostConnection = (hostConnection ?? HostConnectionOptions.Default).Clone();
            return this;
        }

        /// <summary>
        /// Configures runtime-owned host connection config loading from the provided assembly.
        /// </summary>
        /// <param name="bundledConfigAssembly">Assembly containing resources named <c>ansight.developer-pairing.json</c> and/or <c>ansight.json</c>.</param>
        /// <param name="configReader">Optional platform-owned config reader.</param>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithBundledHostConnection(
            Assembly bundledConfigAssembly,
            IHostConnectionConfigReader? configReader = null)
        {
            ArgumentNullException.ThrowIfNull(bundledConfigAssembly);

            return ConfigureHostConnection(hostConnection =>
            {
                hostConnection.UseBundledConfigAssembly(bundledConfigAssembly);
                if (configReader is not null)
                {
                    hostConnection.UseConfigReader(configReader);
                }
            });
        }

        /// <summary>
        /// Configures runtime-owned host connection config loading from a shared text asset loader.
        /// </summary>
        /// <param name="bundledAssetLoader">Loader that resolves bundled text assets by logical asset name.</param>
        /// <param name="configReader">Optional platform-owned config reader.</param>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithBundledHostConnection(
            HostConnectionBundledAssetLoader bundledAssetLoader,
            IHostConnectionConfigReader? configReader = null)
        {
            ArgumentNullException.ThrowIfNull(bundledAssetLoader);

            return ConfigureHostConnection(hostConnection =>
            {
                hostConnection.UseBundledTextAssets(bundledAssetLoader);
                if (configReader is not null)
                {
                    hostConnection.UseConfigReader(configReader);
                }
            });
        }

        /// <summary>
        /// Mutates the runtime-owned host connection configuration in-place.
        /// </summary>
        public OptionsBuilder ConfigureHostConnection(Action<HostConnectionOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            options.HostConnection ??= HostConnectionOptions.Default.Clone();
            configure(options.HostConnection);
            return this;
        }

        /// <summary>
        /// Configures the UDP discovery port used for runtime-owned host connections.
        /// </summary>
        /// <param name="discoveryPort">UDP discovery port to use for initial host discovery.</param>
        /// <returns>The current builder.</returns>
        public OptionsBuilder WithHostConnectionDiscoveryPort(int discoveryPort)
        {
            return ConfigureHostConnection(hostConnection => hostConnection.UseDiscoveryPort(discoveryPort));
        }

        /// <summary>
        /// Validates and returns the configured options.
        /// </summary>
        public Options Build()
        {
            options.Validate();
            return options;
        }
    }
}
