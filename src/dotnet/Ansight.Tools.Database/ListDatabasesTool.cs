namespace Ansight.Tools.Database;

public sealed class ListDatabasesTool : ITool
{
    private const int DefaultMaxResults = 200;

    public string Category => "data";

    public ToolScope Scope => ToolScope.Read;

    public string Id => "data.list_databases";

    public string Name => "List Databases";

    public string Description => "Lists the known app databases that can be inspected.";

    public string Keywords => "database sqlite storage schema";

    public ToolSchema ArgumentsSchema => DatabaseToolSchemas.ListDatabasesArguments;

    public ToolSchema ResultSchema => DatabaseToolSchemas.ListDatabasesResult;

    public Task<ToolResult> Execute(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            var includeSystemStores = DatabaseSupport.GetBoolean(arguments, "includeSystemStores", defaultValue: false);
            var maxResults = DatabaseSupport.GetInt(arguments, "maxResults", defaultValue: DefaultMaxResults, minimum: 1, maximum: 1000);
            return Task.FromResult(ToolResult.Success(DatabaseSupport.ListDatabases(includeSystemStores, maxResults)));
        }
        catch (Exception exception)
        {
            return Task.FromResult(ToolResult.Failure(exception.Message, errorCode: "database_list_failed"));
        }
    }
}
