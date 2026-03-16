namespace Ansight.Tools.FileSystem;

using System;

public static class FileSystemOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithFileSystemTools(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddTools(new ITool[]
        {
            new ListDirectoryTool(),
            new ReadFileTool()
        });
    }
}
