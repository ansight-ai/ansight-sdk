using System.Reflection;

namespace Ansight;

/// <summary>
/// Configures runtime-owned Ansight host connection config resolution.
/// </summary>
public sealed class HostConnectionOptions
{
    /// <summary>
    /// Logical name for the bundled developer pairing config embedded resource.
    /// </summary>
    public const string BundledDeveloperConfigAssetName = "ansight.developer-pairing.json";

    /// <summary>
    /// Logical name for the bundled config embedded resource.
    /// </summary>
    public const string BundledConfigAssetName = "ansight.json";

    /// <summary>
    /// Default host connection configuration.
    /// </summary>
    public static HostConnectionOptions Default { get; } = new();

    /// <summary>
    /// Default retention window for remembered host connection profiles.
    /// </summary>
    public static TimeSpan DefaultConnectionProfileRetention { get; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Optional absolute path for the saved config store.
    /// When omitted, Ansight stores the saved config under local application data for the current app.
    /// </summary>
    public string? SavedConfigPath { get; set; }

    /// <summary>
    /// How long remembered host connection profiles are retained after a successful connection.
    /// Successful reconnects refresh this timer for the associated Wi-Fi profile.
    /// </summary>
    public TimeSpan ConnectionProfileRetention { get; set; } = DefaultConnectionProfileRetention;

    /// <summary>
    /// Optional UDP discovery port override for runtime-owned host connections.
    /// When omitted, Ansight prefers a discovery hint port, then any legacy config port, then the default protocol port.
    /// </summary>
    public int? DiscoveryPort { get; set; }

    /// <summary>
    /// Explicitly permits the insecure protocol-v1 UDP and WebSocket transport.
    /// This should be enabled only for local development migration.
    /// </summary>
    public bool AllowInsecureV1 { get; set; }

    /// <summary>
    /// Optional assembly containing embedded bundled config resources.
    /// When supplied, the SDK looks for embedded resources whose logical names are exactly
    /// <c>ansight.developer-pairing.json</c> and <c>ansight.json</c>.
    /// </summary>
    public Assembly? BundledConfigAssembly { get; set; }

    /// <summary>
    /// Optional loader for a bundled developer pairing config.
    /// When set, this overrides loading <c>ansight.developer-pairing.json</c> from <see cref="BundledConfigAssembly"/>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledDeveloperConfigLoader { get; set; }

    /// <summary>
    /// Optional loader for a bundled config.
    /// When set, this overrides loading <c>ansight.json</c> from <see cref="BundledConfigAssembly"/>.
    /// </summary>
    public Func<CancellationToken, Task<string?>>? BundledConfigLoader { get; set; }

    /// <summary>
    /// Optional platform-owned reader used to obtain config payloads from sources such as files or QR scanners.
    /// </summary>
    public IHostConnectionConfigReader? ConfigReader { get; set; }

    /// <summary>
    /// Configures the assembly used to resolve bundled config resources.
    /// </summary>
    /// <param name="bundledConfigAssembly">Assembly containing resources named <c>ansight.developer-pairing.json</c> and/or <c>ansight.json</c>.</param>
    /// <returns>The current options instance.</returns>
    public HostConnectionOptions UseBundledConfigAssembly(Assembly bundledConfigAssembly)
    {
        BundledConfigAssembly = bundledConfigAssembly ?? throw new ArgumentNullException(nameof(bundledConfigAssembly));
        return this;
    }

    /// <summary>
    /// Configures a shared bundled asset loader for the standard developer and bundled pairing asset names.
    /// </summary>
    /// <param name="bundledAssetLoader">Loader that resolves bundled text assets by logical asset name.</param>
    /// <returns>The current options instance.</returns>
    public HostConnectionOptions UseBundledTextAssets(HostConnectionBundledAssetLoader bundledAssetLoader)
    {
        ArgumentNullException.ThrowIfNull(bundledAssetLoader);

        BundledDeveloperConfigLoader = cancellationToken => bundledAssetLoader(BundledDeveloperConfigAssetName, cancellationToken);
        BundledConfigLoader = cancellationToken => bundledAssetLoader(BundledConfigAssetName, cancellationToken);
        return this;
    }

    /// <summary>
    /// Configures the platform-owned config reader.
    /// </summary>
    /// <param name="configReader">Reader used to obtain config payloads from file pickers, QR scanners, and similar surfaces.</param>
    /// <returns>The current options instance.</returns>
    public HostConnectionOptions UseConfigReader(IHostConnectionConfigReader configReader)
    {
        ConfigReader = configReader ?? throw new ArgumentNullException(nameof(configReader));
        return this;
    }

    /// <summary>
    /// Configures how long remembered host connection profiles are retained.
    /// </summary>
    /// <param name="retention">Positive retention window for remembered host connection profiles.</param>
    /// <returns>The current options instance.</returns>
    public HostConnectionOptions UseConnectionProfileRetention(TimeSpan retention)
    {
        if (retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retention), "Connection profile retention must be positive.");
        }

        ConnectionProfileRetention = retention;
        return this;
    }

    /// <summary>
    /// Configures the UDP discovery port override used for runtime-owned host connections.
    /// </summary>
    /// <param name="discoveryPort">UDP discovery port to use for initial host discovery.</param>
    /// <returns>The current options instance.</returns>
    public HostConnectionOptions UseDiscoveryPort(int discoveryPort)
    {
        if (discoveryPort is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(discoveryPort), "Discovery port must be between 1 and 65535.");
        }

        DiscoveryPort = discoveryPort;
        return this;
    }

    /// <summary>
    /// Enables insecure protocol-v1 compatibility for local development.
    /// </summary>
    public HostConnectionOptions AllowInsecureProtocolV1()
    {
        AllowInsecureV1 = true;
        return this;
    }

    internal HostConnectionOptions Clone()
    {
        return new HostConnectionOptions
        {
            SavedConfigPath = SavedConfigPath,
            ConnectionProfileRetention = ConnectionProfileRetention,
            DiscoveryPort = DiscoveryPort,
            AllowInsecureV1 = AllowInsecureV1,
            BundledConfigAssembly = BundledConfigAssembly,
            BundledDeveloperConfigLoader = BundledDeveloperConfigLoader,
            BundledConfigLoader = BundledConfigLoader,
            ConfigReader = ConfigReader
        };
    }
}
