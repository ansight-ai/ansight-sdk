namespace Ansight;

/// <summary>
/// Controls durable native crash capture and next-launch delivery.
/// </summary>
public sealed class CrashCaptureOptions
{
    public bool Enabled { get; set; } = true;

    public bool StudioHandoffEnabled { get; set; } = true;

    public bool OfflineCaptureAttachmentEnabled { get; set; } = true;

    public int MaximumPendingReports { get; set; } = 8;

    public int RetentionDays { get; set; } = 7;

    public int MaximumBreadcrumbs { get; set; } = 64;

    public int MaximumTraceBytes { get; set; } = 1_048_576;

    internal CrashCaptureOptions Clone()
    {
        return new CrashCaptureOptions
        {
            Enabled = Enabled,
            StudioHandoffEnabled = StudioHandoffEnabled,
            OfflineCaptureAttachmentEnabled = OfflineCaptureAttachmentEnabled,
            MaximumPendingReports = MaximumPendingReports,
            RetentionDays = RetentionDays,
            MaximumBreadcrumbs = MaximumBreadcrumbs,
            MaximumTraceBytes = MaximumTraceBytes
        };
    }

    internal void Validate()
    {
        MaximumPendingReports = Math.Clamp(MaximumPendingReports, 1, 32);
        RetentionDays = Math.Clamp(RetentionDays, 1, 30);
        MaximumBreadcrumbs = Math.Clamp(MaximumBreadcrumbs, 0, 256);
        MaximumTraceBytes = Math.Clamp(MaximumTraceBytes, 16 * 1024, 4 * 1024 * 1024);
    }
}
