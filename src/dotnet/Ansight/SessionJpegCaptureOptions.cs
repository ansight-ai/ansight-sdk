namespace Ansight;

/// <summary>
/// Configures periodic JPEG capture of the app's own root surface while an Ansight pairing session is open.
/// JPEG capture renders the app surface and compresses it for transport, so enabling it can negatively affect runtime performance.
/// </summary>
public sealed class SessionJpegCaptureOptions
{
    /// <summary>
    /// Capture interval in milliseconds. Lower intervals increase capture frequency and are more likely to impact runtime performance.
    /// </summary>
    public ushort IntervalMilliseconds { get; set; } = 2000;

    /// <summary>
    /// JPEG encoding quality from 1 to 100. Higher quality generally increases encoding cost and transport size.
    /// </summary>
    public int Quality { get; set; } = 60;

    /// <summary>
    /// Optional maximum output width in pixels. When null, the full window width is used. Larger widths generally increase capture cost.
    /// </summary>
    public int? MaxWidth { get; set; } = 720;
}
