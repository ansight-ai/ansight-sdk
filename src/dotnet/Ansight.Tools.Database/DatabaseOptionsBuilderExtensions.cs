namespace Ansight.Tools.Database;

using System;

public static class DatabaseOptionsBuilderExtensions
{
    public static Options.OptionsBuilder WithDatabaseTools(this Options.OptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddTools(new ITool[]
        {
            new ListDatabasesTool(),
            new DescribeSchemaTool(),
            new QueryDatabaseTool()
        });
    }
}
