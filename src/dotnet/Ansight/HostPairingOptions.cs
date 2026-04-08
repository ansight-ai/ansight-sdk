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
    /// Optional UDP discovery port override for runtime-owned host pairing connections.
    /// When omitted, Ansight prefers a discovery hint port, then any legacy config port, then the default protocol port.
    /// </summary>
    public int? DiscoveryPort { get; set; }

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

    /// <summary>
    /// Configures the assembly used to resolve bundled pairing resources.
    /// </summary>
    /// <param name="bundledProfileAssembly">Assembly containing resources named <c>ansight.developer-pairing.json</c> and/or <c>ansight.json</c>.</param>
    /// <returns>The current options instance.</returns>
    public HostPairingOptions UseBundledProfileAssembly(Assembly bundledProfileAssembly)
    {
        BundledProfileAssembly = bundledProfileAssembly ?? throw new ArgumentNullException(nameof(bundledProfileAssembly));
        return this;
    }

    /// <summary>
    /// Configures a shared bundled asset loader for the standard developer and bundled pairing asset names.
    /// </summary>
    /// <param name="bundledAssetLoader">Loader that resolves bundled text assets by logical asset name.</param>
    /// <returns>The current options instance.</returns>
    public HostPairingOptions UseBundledTextAssets(HostPairingBundledAssetLoader bundledAssetLoader)
    {
        ArgumentNullException.ThrowIfNull(bundledAssetLoader);

        BundledDeveloperProfileLoader = cancellationToken => bundledAssetLoader(BundledDeveloperAssetName, cancellationToken);
        BundledProfileLoader = cancellationToken => bundledAssetLoader(BundledAssetName, cancellationToken);
        return this;
    }

    /// <summary>
    /// Configures the platform-owned pairing payload reader.
    /// </summary>
    /// <param name="payloadReader">Payload reader used to obtain pairing payloads from file pickers, QR scanners, and similar surfaces.</param>
    /// <returns>The current options instance.</returns>
    public HostPairingOptions UsePayloadReader(IHostPairingPayloadReader payloadReader)
    {
        PayloadReader = payloadReader ?? throw new ArgumentNullException(nameof(payloadReader));
        return this;
    }

    /// <summary>
    /// Configures the UDP discovery port override used for runtime-owned pairing connections.
    /// </summary>
    /// <param name="discoveryPort">UDP discovery port to use for the initial pairing bootstrap.</param>
    /// <returns>The current options instance.</returns>
    public HostPairingOptions UseDiscoveryPort(int discoveryPort)
    {
        if (discoveryPort is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), "Discovery port must be between 1 and 65535.");
        }

        DiscoveryPort = discoveryPort;
        return this;
    }

    internal HostPairingOptions Clone()
    {
        return new HostPairingOptions
        {
            PreferredProfilePath = PreferredProfilePath,
            DiscoveryPort = DiscoveryPort,
            BundledProfileAssembly = BundledProfileAssembly,
            BundledDeveloperProfileLoader = BundledDeveloperProfileLoader,
            BundledProfileLoader = BundledProfileLoader,
            PayloadReader = PayloadReader
        };
    }
}
