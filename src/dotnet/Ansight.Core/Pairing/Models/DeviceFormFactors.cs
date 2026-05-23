namespace Ansight.Pairing.Models;

/// <summary>
/// Normalized device form-factor values used by <see cref="DeviceProfile.FormFactor"/>.
/// </summary>
public static class DeviceFormFactors
{
    /// <summary>
    /// Phone or phone-sized handheld device.
    /// </summary>
    public const string Phone = "phone";

    /// <summary>
    /// Tablet or tablet-sized handheld device.
    /// </summary>
    public const string Tablet = "tablet";

    /// <summary>
    /// Desktop or laptop-class device.
    /// </summary>
    public const string Desktop = "desktop";

    /// <summary>
    /// Television or set-top device.
    /// </summary>
    public const string Tv = "tv";

    /// <summary>
    /// Watch-class device.
    /// </summary>
    public const string Watch = "watch";

    /// <summary>
    /// Automotive or CarPlay-class device.
    /// </summary>
    public const string Car = "car";

    /// <summary>
    /// Virtual-reality or headset-class device.
    /// </summary>
    public const string Vr = "vr";

    /// <summary>
    /// Form factor could not be normalized.
    /// </summary>
    public const string Unknown = "unknown";
}
