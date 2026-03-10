using System.Drawing;
namespace Ansight;

/// <summary>
/// Common constants used across the Ansight telemetry runtime.
/// </summary>
public static class Constants
{
    internal const string LoggingPrefix = " 🟠ANSIGHT: ";

    public const AppEventType DefaultEventType = AppEventType.Info;

    public const ushort DefaultSampleFrequencyMilliseconds = 500;

    public const ushort MinSampleFrequencyMilliseconds = 200;

    public const ushort MaxSampleFrequencyMilliseconds = 2000;

    public const ushort DefaultRetentionPeriodSeconds = 10 * 60;

    public const ushort MinRetentionPeriodSeconds = 1 * 60;

    public const ushort MaxRetentionPeriodSeconds = 60 * 60;

    /// <summary>
    /// Reserved channel identifiers and metadata.
    /// </summary>
    public static class ReservedChannels
    {
        public const byte ClrMemoryUsage_Id = 0;
        public const byte FramesPerSecond_Id = 3;

        public const byte ChannelNotSpecified_Id = byte.MaxValue;

        public static readonly Color ClrMemoryUsage_Color = Color.FromArgb(92, 45, 144);
        public const string ClrMemoryUsage_Name = ".NET";

        public const string FramesPerSecond_Name = "FPS";
        public static readonly Color FramesPerSecond_Color = Color.FromArgb(35, 181, 115);

#if IOS || MACCATALYST
        public const byte PlatformMemoryUsage_Id = 1;
        public const string PlatformMemoryUsage_Name = "Physical Footprint";
        public static readonly Color PlatformMemoryUsage_Color = Color.FromArgb(0, 122, 255);
#elif ANDROID
        public const byte NativeHeapAllocated_Id = 1;
        public const byte Rss_Id = 2;

        public const string NativeHeapAllocated_Name = "Native heap";
        public const string Rss_Name = "RSS";

        public static readonly Color NativeHeapAllocated_Color = Color.FromArgb(0, 122, 255);
        public static readonly Color Rss_Color = Color.FromArgb(200, 140, 30);
#else
        public static readonly Color PlatformMemoryUsage_Color = Color.FromArgb(92, 45, 144);
        public const string PlatformMemoryUsage_Name = "Not Applicable";
#endif
    }

    public static bool IsPredefinedChannel(Channel channel)
    {
        if (channel == null) throw new ArgumentNullException(nameof(channel));

        return channel.Id == Constants.ReservedChannels.ClrMemoryUsage_Id
            || channel.Id == Constants.ReservedChannels.FramesPerSecond_Id
#if IOS || MACCATALYST
            || channel.Id == Constants.ReservedChannels.PlatformMemoryUsage_Id
#elif ANDROID
            || channel.Id == Constants.ReservedChannels.NativeHeapAllocated_Id
            || channel.Id == Constants.ReservedChannels.Rss_Id
#endif
            || channel.Id == Constants.ReservedChannels.ChannelNotSpecified_Id;
    }
}
