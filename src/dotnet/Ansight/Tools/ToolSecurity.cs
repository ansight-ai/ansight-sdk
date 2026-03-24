namespace Ansight.Tools;

using System.Text.Json.Nodes;

public sealed record ToolSecurity(
    ToolSecurityLevel Level,
    string Summary,
    IReadOnlyList<string> Implications)
{
    public static ToolSecurity Unspecified { get; } = new(
        ToolSecurityLevel.Unspecified,
        string.Empty,
        Array.Empty<string>());

    public ToolSecurity(ToolSecurityLevel level, string summary, params string[] implications)
        : this(level, summary, (IReadOnlyList<string>)(implications ?? Array.Empty<string>()))
    {
    }

    public bool IsSpecified =>
        Level != ToolSecurityLevel.Unspecified ||
        !string.IsNullOrWhiteSpace(Summary) ||
        Implications.Count > 0;

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
