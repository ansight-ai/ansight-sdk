namespace Ansight.Pairing.Models;

/// <summary>
/// Metadata for the Ansight SDK that produced the profile.
/// </summary>
public sealed class DeviceSdkProfile
{
    /// <summary>
    /// Human-readable SDK name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// SDK package identifier when known.
    /// </summary>
    public string? PackageId { get; set; }

    /// <summary>
    /// SDK version string.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// SDK implementation language or runtime family.
    /// </summary>
    public string? Language { get; set; }
}
