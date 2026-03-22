using System.Reflection;

namespace Ansight;

/// <summary>
/// Configures runtime-owned Ansight host pairing profile resolution.
/// </summary>
public sealed class HostPairingOptions
{
    /// <summary>
    /// Logical name for the bundled developer pairing embedded resource.
    /// </summary>
    public const string BundledDeveloperAssetName = "ansight.developer-pairing.json";

    /// <summary>
    /// Logical name for the bundled pairing embedded resource.
    /// </summary>
    public const string BundledAssetName = "ansight.json";

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
    /// Optional assembly containing embedded bundled pairing resources.
    /// When supplied, the SDK looks for embedded resources whose logical names are exactly
    /// <c>ansight.developer-pairing.json</c> and <c>ansight.json</c>.
    /// </summary>
    public Assembly? BundledProfileAssembly { get; set; }

    /// <summary>
    /// Optional loader for a bundled developer pairing bootstrap document.
    /// When set, this overrides loading <c>ansight.developer-pairing.json</c> from <see cref="BundledProfileAssembly"/>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledDeveloperProfileLoader { get; set; }

    /// <summary>
    /// Optional loader for a bundled pairing document.
    /// When set, this overrides loading <c>ansight.json</c> from <see cref="BundledProfileAssembly"/>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledProfileLoader { get; set; }

    /// <summary>
    /// Optional platform-owned reader used to obtain pairing payloads from sources such as files or QR scanners.
    /// </summary>
    public IHostPairingPayloadReader? PayloadReader { get; set; }

    internal HostPairingOptions Clone()
    {
        return new HostPairingOptions
        {
            PreferredProfilePath = PreferredProfilePath,
            BundledProfileAssembly = BundledProfileAssembly,
            BundledDeveloperProfileLoader = BundledDeveloperProfileLoader,
            BundledProfileLoader = BundledProfileLoader,
            PayloadReader = PayloadReader
        };
    }
}
