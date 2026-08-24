namespace Ansight.Tools;

/// <summary>
/// Ordered authorization policy required to discover and execute a tool.
/// A grant includes every policy at or below its maximum value.
/// </summary>
public enum ToolPolicy
{
    /// <summary>Allows inspection without changing app state.</summary>
    Read = 0,

    /// <summary>Allows ordinary app and UI state changes.</summary>
    Write = 1,

    /// <summary>Allows destructive, secret-bearing, or code-invoking operations.</summary>
    Critical = 2
}
