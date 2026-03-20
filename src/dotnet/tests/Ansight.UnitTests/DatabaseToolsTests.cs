using Ansight.Tools.Database;
using System.Text.Json.Nodes;

namespace Ansight.UnitTests;

[Collection("DatabaseTools")]
public sealed class DatabaseToolsTests
{
    [Fact]
    public void WithDatabaseTools_RegistersExpectedTools()
    {
        var options = Options.CreateBuilder()
            .WithDatabaseTools()
            .Build();

        Assert.Equal(
            ["data.list_databases", "data.describe_schema", "data.query"],
            options.Tools.Select(tool => tool.Id));
    }

    [Fact]
    public async Task ListDatabasesTool_Execute_FindsDatabasesInsideOverriddenRoots()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            Path.Combine("sandbox", "app.sqlite"),
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new ListDatabasesTool().Execute(new Dictionary<string, string>
        {
            ["maxResults"] = "10"
        });

        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorCode);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var databases = Assert.IsType<JsonArray>(payload["databases"]);
        var databaseEntry = databases
            .Select(node => Assert.IsType<JsonObject>(node))
            .Single(entry => string.Equals(entry["path"]?.GetValue<string>(), databasePath, StringComparison.Ordinal));

        Assert.Equal("app.sqlite", databaseEntry["name"]?.GetValue<string>());
        Assert.Equal("appData", databaseEntry["rootAlias"]?.GetValue<string>());
        Assert.Equal(Path.Combine("sandbox", "app.sqlite"), databaseEntry["relativePath"]?.GetValue<string>());
        Assert.False(payload["truncated"]!.GetValue<bool>());
    }

    [Fact]
    public async Task DescribeSchemaTool_Execute_UsesDatabaseAliasAndAppliesTableFilter()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER);",
            "CREATE TABLE audit_log (id INTEGER PRIMARY KEY, action TEXT NOT NULL);");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new DescribeSchemaTool().Execute(new Dictionary<string, string>
        {
            ["database"] = databasePath,
            ["table"] = "users"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal(databasePath, payload["databasePath"]?.GetValue<string>());

        var tables = Assert.IsType<JsonArray>(payload["tables"]);
        var table = Assert.Single(tables.Select(node => Assert.IsType<JsonObject>(node)));
        Assert.Equal("users", table["name"]?.GetValue<string>());

        var columns = Assert.IsType<JsonArray>(table["columns"]);
        var columnNames = columns
            .Select(node => Assert.IsType<JsonObject>(node)["name"]!.GetValue<string>())
            .ToArray();

        Assert.Equal(["id", "name", "age"], columnNames);
    }

    [Fact]
    public async Task QueryDatabaseTool_Execute_ReturnsRowsAndTruncationMetadata()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);",
            "INSERT INTO users (name) VALUES ('Ada');",
            "INSERT INTO users (name) VALUES ('Grace');",
            "INSERT INTO users (name) VALUES ('Linus');");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new QueryDatabaseTool().Execute(new Dictionary<string, string>
        {
            ["path"] = databasePath,
            ["sql"] = "SELECT id, name FROM users ORDER BY id",
            ["maxRows"] = "2"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        Assert.Equal(databasePath, payload["databasePath"]?.GetValue<string>());
        Assert.Equal("SELECT id, name FROM users ORDER BY id", payload["sql"]?.GetValue<string>());
        Assert.True(payload["truncated"]!.GetValue<bool>());

        var columns = Assert.IsType<JsonArray>(payload["columns"]).Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["id", "name"], columns);

        var rows = Assert.IsType<JsonArray>(payload["rows"]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Ada", Assert.IsType<JsonObject>(rows[0])["name"]?.GetValue<string>());
        Assert.Equal("Grace", Assert.IsType<JsonObject>(rows[1])["name"]?.GetValue<string>());
    }

    [Fact]
    public async Task QueryDatabaseTool_Execute_RejectsWriteStatements()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new QueryDatabaseTool().Execute(new Dictionary<string, string>
        {
            ["path"] = databasePath,
            ["sql"] = "DELETE FROM users"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("database_query_failed", result.ErrorCode);
        Assert.Contains("read-only", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DatabaseRootsOverrideScope : IDisposable
    {
        private readonly Func<IEnumerable<(string Alias, string? Path)>>? previousRootsOverride;

        public DatabaseRootsOverrideScope(params (string Alias, string Path)[] roots)
        {
            previousRootsOverride = DatabaseSupport.PlatformRootsOverride;
            DatabaseSupport.PlatformRootsOverride = () => roots.Select(root => (root.Alias, Path: (string?)root.Path));
        }

        public void Dispose()
        {
            DatabaseSupport.PlatformRootsOverride = previousRootsOverride;
        }
    }
}
