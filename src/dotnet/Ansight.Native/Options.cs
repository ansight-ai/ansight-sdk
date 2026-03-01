namespace Ansight;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Configures how Ansight samples, retains, logs, and presents runtime data.
/// </summary>
public class Options
{
    /// <summary>
    /// Default options instance: 500ms sampling, 10-minute retention, no shake gesture, overlay top right, FPS off.
    /// </summary>
    public static readonly Options Default = new Options()
    {
        SampleFrequencyMilliseconds = Constants.DefaultSampleFrequencyMilliseconds,
        RetentionPeriodSeconds = Constants.DefaultRetentionPeriodSeconds,
        AdditionalChannels = new List<Channel>(),
        DefaultMemoryChannels = DefaultMemoryChannels.PlatformDefaults,
        AllowShakeGesture = false,
        ShakeGestureBehaviour = ShakeGestureBehaviour.SlideSheet,
        AdditionalLogger = new ConsoleLogger(),
        DefaultOverlayPosition= OverlayPosition.TopRight,
        EnableFramesPerSecond = true,
        AppEventRenderingBehaviour = AppEventRenderingBehaviour.IconsOnly,
        ChartTheme = ChartTheme.Dark
    };
    
    /// <summary>
    /// Sampling cadence in milliseconds.
    /// </summary>
    public ushort SampleFrequencyMilliseconds { get; private set; } =  Constants.DefaultSampleFrequencyMilliseconds;
    
    /// <summary>
    /// How long metric/event samples are retained before trim, in seconds.
    /// </summary>
    public ushort RetentionPeriodSeconds { get; private set; } =  Constants.DefaultRetentionPeriodSeconds;
    
    /// <summary>
    /// Maximum buffered samples calculated from retention and frequency.
    /// </summary>
    public int MaximumBufferSize => RetentionPeriodSeconds * (int)Math.Ceiling(1000f / (float)SampleFrequencyMilliseconds);
    
    /// <summary>
    /// Additional metric/event channels to plot besides the built-in ones.
    /// </summary>
    public List<Channel> AdditionalChannels { get; private set; } = new();
    
    /// <summary>
    /// Controls which of the built-in memory channels should be exposed.
    /// </summary>
    public DefaultMemoryChannels DefaultMemoryChannels { get; private set; } = DefaultMemoryChannels.PlatformDefaults;

    /// <summary>
    /// Allow shake gesture to present the UI.
    /// </summary>
    public bool AllowShakeGesture { get; private set; } = false;
    
    /// <summary>
    /// Optional predicate that determines whether the shake gesture should be considered active.
    /// </summary>
    public Func<bool>? ShakeGesturePredicate { get; private set; }
    
    /// <summary>
    /// Behaviour applied when a shake is detected.
    /// </summary>
    public ShakeGestureBehaviour ShakeGestureBehaviour { get; private set; } = ShakeGestureBehaviour.SlideSheet;

    /// <summary>
    /// Enable capturing and rendering frames-per-second metrics at startup.
    /// </summary>
    public bool EnableFramesPerSecond { get; private set; } = false;
    
    /// <summary>
    /// Default overlay anchor position when presented without an explicit position.
    /// </summary>
    public OverlayPosition DefaultOverlayPosition { get; private set; } = OverlayPosition.TopRight;
    
    /// <summary>
    /// Optional additional logger to receive Ansight log messages.
    /// </summary>
    public ILogCallback? AdditionalLogger { get; private set; }
    
    /// <summary>
    /// Configures how annotated events should appear on the chart.
    /// </summary>
    public AppEventRenderingBehaviour AppEventRenderingBehaviour { get; private set; } = AppEventRenderingBehaviour.IconsOnly;

    /// <summary>
    /// Visual theme used when rendering the chart.
    /// </summary>
    public ChartTheme ChartTheme { get; private set; } = ChartTheme.Dark;

    /// <summary>
    /// Optional save snapshot action rendered in the slide sheet.
    /// </summary>
    public SaveSnapshotAction? SaveSnapshotAction { get; internal set; }

    /// <summary>
    /// Provides the native window/activity handle Ansight should use for presentation.
    /// On iOS/Mac Catalyst this can default to the key window. On Android this must be provided.
    /// </summary>
    public Func<object?>? PresentationWindowProvider { get; private set; }

    public void Validate()
    {
        if (SampleFrequencyMilliseconds > Constants.MaxSampleFrequencyMilliseconds)
        {
            Logger.Warning($"The 'SampleFrequencyMilliseconds' was above the minimum frequency of '{Constants.MaxSampleFrequencyMilliseconds}' milliseconds. The sampling rate has been coerced to '{Constants.MaxSampleFrequencyMilliseconds}'");
            SampleFrequencyMilliseconds =  Constants.MaxSampleFrequencyMilliseconds;
        }

        if (SampleFrequencyMilliseconds < Constants.MinSampleFrequencyMilliseconds)
        {
            Logger.Warning($"The 'SampleFrequencyMilliseconds' was below the minimum frequency of '{Constants.MinSampleFrequencyMilliseconds}' milliseconds. The sampling rate has been coerced to '{Constants.MinSampleFrequencyMilliseconds}'");
            SampleFrequencyMilliseconds =  Constants.MinSampleFrequencyMilliseconds;
        }
        
        if (RetentionPeriodSeconds > Constants.MaxRetentionPeriodSeconds)
        {
            Logger.Warning($"The 'RetentionPeriodSeconds' was above the maximum retention of '{Constants.MaxRetentionPeriodSeconds}' seconds. The retention range has been coerced to '{Constants.MaxRetentionPeriodSeconds}'");
            RetentionPeriodSeconds =  Constants.MaxRetentionPeriodSeconds;
        }
        
        if (RetentionPeriodSeconds < Constants.MinRetentionPeriodSeconds)
        {
            Logger.Warning($"The 'RetentionPeriodSeconds' was below the minimum retention of '{Constants.MinRetentionPeriodSeconds}' seconds. The retention range has been coerced to '{Constants.MinRetentionPeriodSeconds}'");
            RetentionPeriodSeconds =  Constants.MinRetentionPeriodSeconds;
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
                AllowShakeGesture = initialOptions.AllowShakeGesture,
                ShakeGesturePredicate = initialOptions.ShakeGesturePredicate,
                ShakeGestureBehaviour = initialOptions.ShakeGestureBehaviour,
                EnableFramesPerSecond = initialOptions.EnableFramesPerSecond,
                DefaultOverlayPosition = initialOptions.DefaultOverlayPosition,
                AdditionalLogger = initialOptions.AdditionalLogger,
                AppEventRenderingBehaviour = initialOptions.AppEventRenderingBehaviour,
                ChartTheme = initialOptions.ChartTheme,
                SaveSnapshotAction = initialOptions.SaveSnapshotAction,
                PresentationWindowProvider = initialOptions.PresentationWindowProvider
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
        /// Enables frames-per-second sampling and charting at startup.
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
        /// Replaces the additional channel's collection.
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
        /// Specifies which of the built-in memory channels should be displayed.
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
        /// Configures the shake gesture behaviour.
        /// </summary>
        public OptionsBuilder WithShakeGestureBehaviour(ShakeGestureBehaviour  shakeGestureBehaviour)
        {
            options.ShakeGestureBehaviour = shakeGestureBehaviour;
            return this;
        }
        
        /// <summary>
        /// Enables handling of device shake gestures.
        /// </summary>
        public OptionsBuilder WithShakeGesture()
        {
            options.AllowShakeGesture = true;
            return this;
        }

        /// <summary>
        /// Configures a predicate evaluated before enabling or responding to shake gestures.
        /// </summary>
        public OptionsBuilder WithShakeGesturePredicate(Func<bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            
            options.ShakeGesturePredicate = predicate;
            return this;
        }

        /// <summary>
        /// Sets the default overlay anchor position used when none is specified.
        /// </summary>
        public OptionsBuilder WithDefaultOverlayPosition(OverlayPosition position)
        {
            options.DefaultOverlayPosition = position;
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
        /// Configures how events are rendered on the chart.
        /// </summary>
        public OptionsBuilder WithEventRenderingBehaviour(AppEventRenderingBehaviour behaviour)
        {
            options.AppEventRenderingBehaviour = behaviour;
            return this;
        }

        /// <summary>
        /// Sets the chart theme.
        /// </summary>
        public OptionsBuilder WithChartTheme(ChartTheme theme)
        {
            options.ChartTheme = theme;
            return this;
        }

        /// <summary>
        /// Sets the chart theme.
        /// </summary>

        /// <summary>
        /// Provides the native window/activity handle Ansight should use for presentation.
        /// On Android this is required. On iOS/Mac Catalyst you may omit to fall back to the key window lookup.
        /// </summary>
        public OptionsBuilder WithPresentationWindowProvider(Func<object?> provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            options.PresentationWindowProvider = provider;
            return this;
        }

        /// <summary>
        /// Enables a custom save snapshot action in the slide sheet.
        /// </summary>
        /// <param name="copyDelegate">Delegate invoked with the captured <see cref="Snapshot"/>.</param>
        /// <param name="label">Text displayed on the action button.</param>
        public OptionsBuilder WithSaveSnapshotAction(Func<Snapshot, Task> copyDelegate, string label)
        {
            if (copyDelegate == null) throw new ArgumentNullException(nameof(copyDelegate));
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(label));

            options.SaveSnapshotAction = new SaveSnapshotAction(label, copyDelegate);
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
