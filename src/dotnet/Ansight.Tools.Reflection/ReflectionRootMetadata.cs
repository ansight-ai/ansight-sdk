namespace Ansight.Tools.Reflection;

public sealed record ReflectionRootMetadata(string DisplayName)
{
    public string? Description { get; init; }

    public string? Category { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public bool? ContainsSensitiveData { get; init; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();
}
