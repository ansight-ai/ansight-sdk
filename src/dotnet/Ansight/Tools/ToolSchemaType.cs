namespace Ansight.Tools;

/// <summary>
/// Value kinds supported by <see cref="ToolSchema"/>.
/// </summary>
public enum ToolSchemaType
{
    /// <summary>
    /// Object value with named properties.
    /// </summary>
    Object = 0,

    /// <summary>
    /// Array value with homogeneous items.
    /// </summary>
    Array = 1,

    /// <summary>
    /// String value.
    /// </summary>
    String = 2,

    /// <summary>
    /// Integer numeric value.
    /// </summary>
    Integer = 3,

    /// <summary>
    /// Floating-point or general numeric value.
    /// </summary>
    Number = 4,

    /// <summary>
    /// Boolean value.
    /// </summary>
    Boolean = 5
}
