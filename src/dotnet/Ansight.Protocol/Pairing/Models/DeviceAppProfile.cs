namespace Ansight.Pairing.Models;

/// <summary>
/// Baseline profile sent to the host to describe the connected app, device, and runtime.
/// </summary>
public sealed class DeviceAppProfile
{
    /// <summary>
    /// Protocol message type identifier.
    /// </summary>
    public string Type { get; set; } = "DeviceAppProfile";

    /// <summary>
    /// Schema identifier for the payload.
    /// </summary>
    public string Schema { get; set; } = "ansight.device-app-profile.v1";

    /// <summary>
    /// Unix timestamp in milliseconds indicating when the profile was sent.
    /// </summary>
    public long SentAt { get; set; }

    /// <summary>
    /// Protocol-defined reason code for why this profile is being sent.
    /// </summary>
    public int ReasonCode { get; set; } = 1;

    /// <summary>
    /// Sequence number for this profile within the session.
    /// </summary>
    public int ProfileSeq { get; set; } = 1;

    /// <summary>
    /// Metadata for the SDK that produced this profile.
    /// </summary>
    public DeviceSdkProfile? Sdk { get; set; }

    /// <summary>
    /// Device-specific metadata.
    /// </summary>
    public DeviceProfile? Device { get; set; }

    /// <summary>
    /// Application-specific metadata.
    /// </summary>
    public DeviceApplicationProfile? App { get; set; }

    /// <summary>
    /// Runtime-specific metadata.
    /// </summary>
    public DeviceRuntimeProfile? Runtime { get; set; }

    /// <summary>
    /// Graphics and rendering metadata.
    /// </summary>
    public DeviceGraphicsProfile? Graphics { get; set; }

    /// <summary>
    /// Optional permission-state metadata keyed by permission name.
    /// </summary>
    public Dictionary<string, string>? Permissions { get; set; }

    /// <summary>
    /// Optional free-form tags associated with the profile.
    /// </summary>
    public List<string>? Tags { get; set; }
}
