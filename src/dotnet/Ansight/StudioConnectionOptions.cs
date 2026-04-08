using System.Reflection;

namespace Ansight;

/// <summary>
/// Configures runtime-owned Ansight Studio connection ticket resolution.
/// </summary>
public sealed class StudioConnectionOptions
{
    /// <summary>
    /// Logical name for the bundled developer pairing ticket embedded resource.
    /// </summary>
    public const string BundledDeveloperTicketAssetName = "ansight.developer-pairing.json";

    /// <summary>
    /// Logical name for the bundled ticket embedded resource.
    /// </summary>
    public const string BundledTicketAssetName = "ansight.json";

    /// <summary>
    /// Default Studio connection configuration.
    /// </summary>
    public static StudioConnectionOptions Default { get; } = new();

    /// <summary>
    /// Optional absolute path for the saved ticket store.
    /// When omitted, Ansight stores the saved ticket under local application data for the current app.
    /// </summary>
    public string? SavedTicketPath { get; set; }

    /// <summary>
    /// Optional UDP discovery port override for runtime-owned Studio connections.
    /// When omitted, Ansight prefers a discovery hint port, then any legacy config port, then the default protocol port.
    /// </summary>
    public int? DiscoveryPort { get; set; }

    /// <summary>
    /// Optional assembly containing embedded bundled ticket resources.
    /// When supplied, the SDK looks for embedded resources whose logical names are exactly
    /// <c>ansight.developer-pairing.json</c> and <c>ansight.json</c>.
    /// </summary>
    public Assembly? BundledTicketAssembly { get; set; }

    /// <summary>
    /// Optional loader for a bundled developer pairing ticket.
    /// When set, this overrides loading <c>ansight.developer-pairing.json</c> from <see cref="BundledTicketAssembly"/>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledDeveloperTicketLoader { get; set; }

    /// <summary>
    /// Optional loader for a bundled ticket.
    /// When set, this overrides loading <c>ansight.json</c> from <see cref="BundledTicketAssembly"/>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledTicketLoader { get; set; }

    /// <summary>
    /// Optional platform-owned reader used to obtain ticket payloads from sources such as files or QR scanners.
    /// </summary>
    public IStudioConnectionTicketReader? TicketReader { get; set; }

    /// <summary>
    /// Configures the assembly used to resolve bundled ticket resources.
    /// </summary>
    /// <param name="bundledTicketAssembly">Assembly containing resources named <c>ansight.developer-pairing.json</c> and/or <c>ansight.json</c>.</param>
    /// <returns>The current options instance.</returns>
    public StudioConnectionOptions UseBundledTicketAssembly(Assembly bundledTicketAssembly)
    {
        BundledTicketAssembly = bundledTicketAssembly ?? throw new ArgumentNullException(nameof(bundledTicketAssembly));
        return this;
    }

    /// <summary>
    /// Configures a shared bundled asset loader for the standard developer and bundled pairing asset names.
    /// </summary>
    /// <param name="bundledAssetLoader">Loader that resolves bundled text assets by logical asset name.</param>
    /// <returns>The current options instance.</returns>
    public StudioConnectionOptions UseBundledTextAssets(StudioConnectionBundledAssetLoader bundledAssetLoader)
    {
        ArgumentNullException.ThrowIfNull(bundledAssetLoader);

        BundledDeveloperTicketLoader = cancellationToken => bundledAssetLoader(BundledDeveloperTicketAssetName, cancellationToken);
        BundledTicketLoader = cancellationToken => bundledAssetLoader(BundledTicketAssetName, cancellationToken);
        return this;
    }

    /// <summary>
    /// Configures the platform-owned ticket reader.
    /// </summary>
    /// <param name="ticketReader">Reader used to obtain ticket payloads from file pickers, QR scanners, and similar surfaces.</param>
    /// <returns>The current options instance.</returns>
    public StudioConnectionOptions UseTicketReader(IStudioConnectionTicketReader ticketReader)
    {
        TicketReader = ticketReader ?? throw new ArgumentNullException(nameof(ticketReader));
        return this;
    }

    /// <summary>
    /// Configures the UDP discovery port override used for runtime-owned Studio connections.
    /// </summary>
    /// <param name="discoveryPort">UDP discovery port to use for initial host discovery.</param>
    /// <returns>The current options instance.</returns>
    public StudioConnectionOptions UseDiscoveryPort(int discoveryPort)
    {
        if (discoveryPort is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), "Discovery port must be between 1 and 65535.");
        }

        DiscoveryPort = discoveryPort;
        return this;
    }

    internal StudioConnectionOptions Clone()
    {
        return new StudioConnectionOptions
        {
            SavedTicketPath = SavedTicketPath,
            DiscoveryPort = DiscoveryPort,
            BundledTicketAssembly = BundledTicketAssembly,
            BundledDeveloperTicketLoader = BundledDeveloperTicketLoader,
            BundledTicketLoader = BundledTicketLoader,
            TicketReader = TicketReader
        };
    }
}
