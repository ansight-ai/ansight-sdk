namespace Ansight.Telemetry.Channels;

public static class MetricGroups
{
    public static MetricGroup Memory { get; } = new MetricGroup("memory", "Memory");
    public static MetricGroup Rendering { get; } = new MetricGroup("rendering", "Rendering");
    public static MetricGroup Layout { get; } = new MetricGroup("layout", "Layout");
    public static MetricGroup Runtime { get; } = new MetricGroup("runtime", "Runtime");
    public static MetricGroup Device { get; } = new MetricGroup("device", "Device");
    public static MetricGroup Network { get; } = new MetricGroup("network", "Network");
}
