namespace Ansight;

using System;
using System.Collections.Generic;
using System.Linq;

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
        EnableFramesPerSecond = true
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
                EnableFramesPerSecond = initialOptions.EnableFramesPerSecond
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
        /// Validates and returns the configured options.
        /// </summary>
        public Options Build()
        {
            options.Validate();
            return options;
        }
    }
}
