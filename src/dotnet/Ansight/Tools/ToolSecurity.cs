namespace Ansight.Tools;

using System.Text.Json.Nodes;

/// <summary>
/// Security metadata associated with a tool definition.
/// </summary>
/// <param name="Level">Overall security sensitivity level for the tool.</param>
/// <param name="Summary">Human-readable explanation of the tool's security implications.</param>
/// <param name="Implications">Canonical implication identifiers describing the sensitive behaviors of the tool.</param>
public sealed record ToolSecurity(
    ToolSecurityLevel Level,
    string Summary,
    IReadOnlyList<string> Implications)
{
    /// <summary>
    /// Default security metadata used when a tool has not declared any specific security information.
    /// </summary>
    public static ToolSecurity Unspecified { get; } = new(
        ToolSecurityLevel.Unspecified,
        string.Empty,
        Array.Empty<string>());

    /// <summary>
    /// Creates tool security metadata from a variable-length list of implication identifiers.
    /// </summary>
    /// <param name="level">Overall security sensitivity level for the tool.</param>
    /// <param name="summary">Human-readable explanation of the tool's security implications.</param>
    /// <param name="implications">Canonical implication identifiers describing the sensitive behaviors of the tool.</param>
    public ToolSecurity(ToolSecurityLevel level, string summary, params string[] implications)
        : this(level, summary, (IReadOnlyList<string>)(implications ?? Array.Empty<string>()))
    {
    }

    /// <summary>
    /// Indicates whether any explicit security metadata has been declared.
    /// </summary>
    public bool IsSpecified =>
        Level != ToolSecurityLevel.Unspecified ||
        !string.IsNullOrWhiteSpace(Summary) ||
        Implications.Count > 0;

    /// <summary>
    /// Converts the security metadata into a JSON object suitable for catalogs and protocol payloads.
    /// </summary>
    /// <returns>JSON representation of the security metadata.</returns>
    public JsonObject ToJson()
    {
        var implications = new JsonArray();
        foreach (var implication in Implications
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            implications.Add(implication);
        }

        return new JsonObject
        {
            ["level"] = Level.ToString(),
            ["summary"] = Summary,
            ["implications"] = implications
        };
    }
}
