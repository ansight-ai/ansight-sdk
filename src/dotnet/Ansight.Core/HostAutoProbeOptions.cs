namespace Ansight;

/// <summary>
/// Controls the background Ansight host auto-probe loop.
/// </summary>
public sealed class HostAutoProbeOptions
{
    /// <summary>
    /// Default enabled configuration used by <see cref="Options.Default"/>.
    /// </summary>
    public static HostAutoProbeOptions EnabledDefault { get; } = new()
    {
        Enabled = true,
        InitialDelay = TimeSpan.FromSeconds(1),
        ProbeInterval = TimeSpan.FromSeconds(5),
        ReconnectDelay = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Default disabled configuration.
    /// </summary>
    public static HostAutoProbeOptions DisabledDefault { get; } = new()
    {
        Enabled = false,
        InitialDelay = TimeSpan.Zero,
        ProbeInterval = TimeSpan.FromSeconds(5),
        ReconnectDelay = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Whether Ansight should automatically probe for a previously paired host when the runtime is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Delay applied after runtime activation before the first probe attempt.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Delay between probe attempts while no active session is connected.
    /// </summary>
    public TimeSpan ProbeInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Delay before probing resumes after an established session disconnects.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Optional client name override used for automatic host connections.
    /// </summary>
    public string? ClientName { get; set; }

    internal HostAutoProbeOptions Clone()
    {
        return new HostAutoProbeOptions
        {
            Enabled = Enabled,
            InitialDelay = InitialDelay,
            ProbeInterval = ProbeInterval,
            ReconnectDelay = ReconnectDelay,
            ClientName = ClientName
        };
    }
}
