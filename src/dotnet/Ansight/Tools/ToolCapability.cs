namespace Ansight.Tools;

/// <summary>
/// Strongly typed capability token used to authorize groups of related tools.
/// </summary>
public readonly record struct ToolCapability
{
    public ToolCapability(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim();
    }

    /// <summary>
    /// Built-in capability for UI inspection tools.
    /// </summary>
    public static ToolCapability Ui => new("ui");

    /// <summary>
    /// Built-in capability for reflection and object inspection tools.
    /// </summary>
    public static ToolCapability Reflection => new("reflect");

    /// <summary>
    /// Built-in capability for database inspection tools.
    /// </summary>
    public static ToolCapability Database => new("data");

    /// <summary>
    /// Built-in capability for filesystem tools.
    /// </summary>
    public static ToolCapability FileSystem => new("files");

    /// <summary>
    /// Built-in capability for preferences tools.
    /// </summary>
    public static ToolCapability Preferences => new("prefs");

    /// <summary>
    /// Built-in capability for secure-storage tools.
    /// </summary>
    public static ToolCapability SecureStorage => new("secure");

    /// <summary>
    /// Creates a capability token from a category-like string.
    /// </summary>
    public static ToolCapability FromCategory(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return new ToolCapability(category.Trim());
    }

    /// <summary>
    /// The normalized token value.
    /// </summary>
    public string Value { get; }

    public override string ToString() => Value;
}
