namespace Ansight.Tools;

/// <summary>
/// Access scopes used by tool guard policies.
/// </summary>
public enum ToolScope
{
    /// <summary>
    /// Read-only inspection or retrieval operations.
    /// </summary>
    Read = 0,

    /// <summary>
    /// Create or update operations that modify app-owned data.
    /// </summary>
    Write = 1,

    /// <summary>
    /// Destructive removal operations.
    /// </summary>
    Delete = 2
}
