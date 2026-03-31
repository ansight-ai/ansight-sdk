namespace Ansight.Tools.Reflection;

public sealed record ReflectionRootMetadata(string DisplayName)
{
    public string? Description { get; init; }

    public IReadOnlyList<string> Hints { get; init; } = Array.Empty<string>();

    public bool? ContainsSensitiveData { get; init; }
}
