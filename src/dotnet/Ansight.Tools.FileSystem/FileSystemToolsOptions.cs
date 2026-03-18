namespace Ansight.Tools.FileSystem;

public sealed class FileSystemToolsOptions
{
    internal static FileSystemToolsOptions Default { get; } = new(Array.Empty<AdditionalFileSystemRoot>());

    internal FileSystemToolsOptions(IReadOnlyList<AdditionalFileSystemRoot> additionalRoots)
    {
        AdditionalRoots = additionalRoots;
    }

    public IReadOnlyList<AdditionalFileSystemRoot> AdditionalRoots { get; }

    public static FileSystemToolsOptionsBuilder CreateBuilder() => new();
}

public sealed class FileSystemToolsOptionsBuilder
{
    private readonly Dictionary<string, AdditionalFileSystemRoot> rootsByTag = new(StringComparer.OrdinalIgnoreCase);

    public FileSystemToolsOptionsBuilder AddRoot(string tag, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        rootsByTag[tag.Trim()] = new AdditionalFileSystemRoot(tag.Trim(), path.Trim());
        return this;
    }

    public FileSystemToolsOptions Build()
        => new(rootsByTag.Values.ToList());
}

public sealed record AdditionalFileSystemRoot(string Tag, string Path);
