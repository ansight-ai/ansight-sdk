namespace Ansight.Tools.FileSystem;

using System;

public static class FileSystemOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithFileSystemTools(this Options.OptionsBuilder builder)
        => builder.WithFileSystemTools(static _ => { });

    public static Options.OptionsBuilder WithFileSystemTools(
        this Options.OptionsBuilder builder,
        Action<FileSystemToolsOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var optionsBuilder = FileSystemToolsOptions.CreateBuilder();
        configure(optionsBuilder);
        var options = optionsBuilder.Build();

        return builder.AddTools(new ITool[]
        {
            new ListDirectoryTool(options),
            new ReadFileTool(options),
            new GetFileChecksumTool(options),
            new DownloadFileTool(options),
            new BeginBinaryDownloadTool(options)
        });
    }
}
