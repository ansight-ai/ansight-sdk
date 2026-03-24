namespace Ansight.Tools.Database;

public sealed class DescribeSchemaTool : ITool
{
    public string Category => "data";

    public ToolScope Scope => ToolScope.Read;

    public string Id => DatabaseToolIds.DescribeSchema;

    public string Name => "Describe Schema";

    public string Description => "Returns schema metadata for a database or table.";

    public string Keywords => "database schema tables columns sqlite";

    public ToolSchema ArgumentsSchema => DatabaseToolSchemas.DescribeSchemaArguments;

    public ToolSchema ResultSchema => DatabaseToolSchemas.DescribeSchemaResult;

    public ToolSecurity Security => DatabaseToolSecurityProfiles.DescribeSchema;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var databasePath = DatabaseSupport.ResolveDatabasePath(arguments);
            var tableFilter = DatabaseSupport.GetString(arguments, "table");
            return Task.FromResult(ToolResult.Success(DatabaseSupport.DescribeSchema(databasePath, tableFilter)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "database_schema_failed"));
        }
    }
}
