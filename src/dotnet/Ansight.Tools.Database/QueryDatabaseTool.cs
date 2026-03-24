namespace Ansight.Tools.Database;

public sealed class QueryDatabaseTool : ITool
{
    private const int DefaultMaxRows = 100;

    public string Category => "data";

    public ToolScope Scope => ToolScope.Read;

    public string Id => DatabaseToolIds.Query;

    public string Name => "Query Database";

    public string Description => "Executes a constrained read query against an app database.";

    public string Keywords => "database sql query read";

    public ToolSchema ArgumentsSchema => DatabaseToolSchemas.QueryArguments;

    public ToolSchema ResultSchema => DatabaseToolSchemas.QueryResult;

    public ToolSecurity Security => DatabaseToolSecurityProfiles.Query;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var databasePath = DatabaseSupport.ResolveDatabasePath(arguments);
            var sql = DatabaseSupport.GetRequiredString(arguments, "sql");
            var maxRows = DatabaseSupport.GetInt(arguments, "maxRows", defaultValue: DefaultMaxRows, minimum: 1, maximum: 1000);

            return Task.FromResult(ToolResult.Success(DatabaseSupport.ExecuteQuery(databasePath, sql, maxRows)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "database_query_failed"));
        }
    }
}
