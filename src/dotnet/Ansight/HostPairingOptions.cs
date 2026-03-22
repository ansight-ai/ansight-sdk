namespace Ansight;

/// <summary>
/// Configures runtime-owned Ansight host pairing profile resolution.
/// </summary>
public sealed class HostPairingOptions
{
    /// <summary>
    /// Default host pairing configuration.
    /// </summary>
    public static HostPairingOptions Default { get; } = new();

    /// <summary>
    /// Optional absolute path for the preferred pairing profile store.
    /// When omitted, Ansight stores the preferred profile under local application data for the current app.
    /// </summary>
    public string? PreferredProfilePath { get; set; }

    /// <summary>
    /// Optional loader for a bundled developer pairing bootstrap document.
    /// This is typically backed by an app-package asset such as <c>ansight.developer-pairing.json</c>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledDeveloperProfileLoader { get; set; }

    /// <summary>
    /// Optional loader for a bundled pairing document.
    /// This is typically backed by an app-package asset such as <c>ansight.json</c>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledProfileLoader { get; set; }

    internal HostPairingOptions Clone()
    {
        return new HostPairingOptions
        {
            PreferredProfilePath = PreferredProfilePath,
            BundledDeveloperProfileLoader = BundledDeveloperProfileLoader,
            BundledProfileLoader = BundledProfileLoader
        };
    }
}
