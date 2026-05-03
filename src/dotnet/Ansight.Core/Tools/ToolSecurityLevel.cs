namespace Ansight.Tools;

/// <summary>
/// Describes the overall security sensitivity of a tool.
/// </summary>
public enum ToolSecurityLevel
{
    /// <summary>
    /// No security level has been declared.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The tool has limited security impact and performs low-risk operations.
    /// </summary>
    Low = 1,

    /// <summary>
    /// The tool performs operations that warrant user awareness and moderate scrutiny.
    /// </summary>
    Moderate = 2,

    /// <summary>
    /// The tool performs sensitive operations that can materially affect app data or visibility.
    /// </summary>
    High = 3,

    /// <summary>
    /// The tool performs highly sensitive operations involving severe data, privacy, or integrity risk.
    /// </summary>
    Critical = 4
}
