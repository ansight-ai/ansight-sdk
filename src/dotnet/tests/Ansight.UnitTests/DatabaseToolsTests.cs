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
            [DatabaseToolIds.ListDatabases, DatabaseToolIds.DescribeSchema, DatabaseToolIds.Query],
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
    public async Task ListDatabasesTool_Execute_IgnoresNonSqliteFilesWithDatabaseExtensions()
    {
        using var tempDirectory = new TemporaryDirectory();
        var fakeDatabasePath = tempDirectory.WriteTextFile("fake.db", "not a sqlite database");
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new ListDatabasesTool().Execute(new Dictionary<string, string>
        {
            ["maxResults"] = "10"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var databasePaths = Assert.IsType<JsonArray>(payload["databases"])
            .Select(node => Assert.IsType<JsonObject>(node)["path"]!.GetValue<string>())
            .ToArray();

        Assert.Contains(databasePath, databasePaths);
        Assert.DoesNotContain(fakeDatabasePath, databasePaths);
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
    public async Task QueryDatabaseTool_Execute_ReturnsColumnMetadataAndTypedStorageValues()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            """
            CREATE TABLE samples (
                id INTEGER PRIMARY KEY,
                integer_value INTEGER,
                real_value REAL,
                text_value TEXT,
                blob_value BLOB,
                null_value TEXT,
                bool_value BOOLEAN,
                date_value DATETIME,
                guid_value UNIQUEIDENTIFIER,
                decimal_value DECIMAL(18,2),
                json_value JSON
            );
            """,
            """
            INSERT INTO samples (
                integer_value,
                real_value,
                text_value,
                blob_value,
                null_value,
                bool_value,
                date_value,
                guid_value,
                decimal_value,
                json_value
            ) VALUES (
                42,
                3.25,
                'hello',
                X'000102FF',
                NULL,
                1,
                '2026-04-21T01:02:03Z',
                '01234567-89ab-cdef-0123-456789abcdef',
                '1234.56',
                '{"ok":true}'
            );
            """);
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new QueryDatabaseTool().Execute(new Dictionary<string, string>
        {
            ["path"] = databasePath,
            ["sql"] = """
                SELECT
                    integer_value,
                    real_value,
                    text_value,
                    blob_value,
                    null_value,
                    bool_value,
                    date_value,
                    guid_value,
                    decimal_value,
                    json_value
                FROM samples
                """
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var columnMetadata = Assert.IsType<JsonArray>(payload["columnMetadata"])
            .Select(node => Assert.IsType<JsonObject>(node))
            .ToArray();

        Assert.Equal("integer_value", columnMetadata[0]["key"]?.GetValue<string>());
        Assert.Equal("INTEGER", columnMetadata[0]["declaredType"]?.GetValue<string>());
        Assert.Equal("BOOLEAN", columnMetadata[5]["declaredType"]?.GetValue<string>());
        Assert.Equal("DATETIME", columnMetadata[6]["declaredType"]?.GetValue<string>());
        Assert.Equal("UNIQUEIDENTIFIER", columnMetadata[7]["declaredType"]?.GetValue<string>());
        Assert.Equal("DECIMAL(18,2)", columnMetadata[8]["declaredType"]?.GetValue<string>());
        Assert.Equal("JSON", columnMetadata[9]["declaredType"]?.GetValue<string>());

        var optionalSourceTable = columnMetadata[0]["sourceTable"]?.GetValue<string>();
        if (optionalSourceTable is not null)
        {
            Assert.Equal("samples", optionalSourceTable);
            Assert.Equal("integer_value", columnMetadata[0]["sourceColumn"]?.GetValue<string>());
        }

        var rows = Assert.IsType<JsonArray>(payload["rows"]);
        var row = Assert.IsType<JsonObject>(Assert.Single(rows));
        Assert.Equal(42, row["integer_value"]?.GetValue<long>());
        Assert.Equal(3.25, row["real_value"]?.GetValue<double>());
        Assert.Equal("hello", row["text_value"]?.GetValue<string>());
        Assert.True(row.TryGetPropertyValue("null_value", out var nullValue));
        Assert.Null(nullValue);
        Assert.Equal(1, row["bool_value"]?.GetValue<long>());
        Assert.Equal("2026-04-21T01:02:03Z", row["date_value"]?.GetValue<string>());
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", row["guid_value"]?.GetValue<string>());
        Assert.Equal("""{"ok":true}""", row["json_value"]?.GetValue<string>());

        var blob = Assert.IsType<JsonObject>(row["blob_value"]);
        Assert.Equal("blob", blob["type"]?.GetValue<string>());
        Assert.Equal("AAEC/w==", blob["base64"]?.GetValue<string>());
        Assert.Equal(4, blob["byteLength"]?.GetValue<int>());

        var rowValues = Assert.IsType<JsonArray>(payload["rowValues"]);
        var cells = Assert.IsType<JsonArray>(Assert.Single(rowValues))
            .Select(node => Assert.IsType<JsonObject>(node))
            .ToArray();
        Assert.Equal(
            ["integer", "real", "text", "blob", "null", "integer", "text", "text"],
            cells.Take(8).Select(cell => cell["storageType"]!.GetValue<string>()));
        Assert.Contains(cells[8]["storageType"]!.GetValue<string>(), new[] { "integer", "real", "text" });
        Assert.Equal("text", cells[9]["storageType"]?.GetValue<string>());
    }

    [Fact]
    public async Task QueryDatabaseTool_Execute_PreservesDuplicateColumnValuesWithUniqueKeys()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);",
            "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL);",
            "INSERT INTO users (id, name) VALUES (1, 'Ada');",
            "INSERT INTO orders (id, user_id) VALUES (10, 1);");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new QueryDatabaseTool().Execute(new Dictionary<string, string>
        {
            ["path"] = databasePath,
            ["sql"] = """
                SELECT users.id, orders.id, users.name
                FROM users
                INNER JOIN orders ON orders.user_id = users.id
                """
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var columns = Assert.IsType<JsonArray>(payload["columns"]).Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["id", "id", "name"], columns);

        var columnKeys = Assert.IsType<JsonArray>(payload["columnMetadata"])
            .Select(node => Assert.IsType<JsonObject>(node)["key"]!.GetValue<string>())
            .ToArray();
        Assert.Equal(["id", "id_2", "name"], columnKeys);

        var rows = Assert.IsType<JsonArray>(payload["rows"]);
        var row = Assert.IsType<JsonObject>(Assert.Single(rows));
        Assert.Equal(1, row["id"]?.GetValue<long>());
        Assert.Equal(10, row["id_2"]?.GetValue<long>());
        Assert.Equal("Ada", row["name"]?.GetValue<string>());

        var rowValues = Assert.IsType<JsonArray>(payload["rowValues"]);
        var cells = Assert.IsType<JsonArray>(Assert.Single(rowValues))
            .Select(node => Assert.IsType<JsonObject>(node))
            .ToArray();
        Assert.Equal(["id", "id_2", "name"], cells.Select(cell => cell["columnKey"]!.GetValue<string>()));
    }

    [Fact]
    public async Task QueryDatabaseTool_Execute_PreservesTextWithEmbeddedNullCharacters()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            "CREATE TABLE samples (value TEXT);",
            "INSERT INTO samples (value) VALUES ('a' || char(0) || 'b');");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new QueryDatabaseTool().Execute(new Dictionary<string, string>
        {
            ["path"] = databasePath,
            ["sql"] = "SELECT value FROM samples"
        });

        Assert.True(result.IsSuccess);

        var payload = Assert.IsType<JsonObject>(result.Payload);
        var rows = Assert.IsType<JsonArray>(payload["rows"]);
        var row = Assert.IsType<JsonObject>(Assert.Single(rows));
        var value = row["value"]?.GetValue<string>();

        Assert.Equal(3, value?.Length);
        Assert.Equal('\0', value?[1]);
        Assert.Equal('b', value?[2]);
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

    [Fact]
    public async Task QueryDatabaseTool_Execute_RejectsMultipleStatements()
    {
        using var tempDirectory = new TemporaryDirectory();
        var databasePath = tempDirectory.CreateSqliteDatabase(
            "app.sqlite",
            "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);",
            "INSERT INTO users (name) VALUES ('Ada');");
        using var rootOverride = new DatabaseRootsOverrideScope(("appData", tempDirectory.RootPath));

        var result = await new QueryDatabaseTool().Execute(new Dictionary<string, string>
        {
            ["path"] = databasePath,
            ["sql"] = "SELECT id, name FROM users ORDER BY id; SELECT COUNT(*) FROM users"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("database_query_failed", result.ErrorCode);
        Assert.Contains("single", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DatabaseRootsOverrideScope : IDisposable
    {
        private readonly Func<IEnumerable<(string Alias, string? Path)>>? previousListRootsOverride;
        private readonly Func<IEnumerable<(string Alias, string? Path)>>? previousDescribeRootsOverride;
        private readonly Func<IEnumerable<(string Alias, string? Path)>>? previousQueryRootsOverride;

        public DatabaseRootsOverrideScope(params (string Alias, string Path)[] roots)
        {
            previousListRootsOverride = ListDatabasesTool.PlatformRootsOverride;
            previousDescribeRootsOverride = DescribeSchemaTool.PlatformRootsOverride;
            previousQueryRootsOverride = QueryDatabaseTool.PlatformRootsOverride;

            IEnumerable<(string Alias, string? Path)> GetRoots()
            {
                return roots.Select(root => (root.Alias, Path: (string?)root.Path));
            }

            ListDatabasesTool.PlatformRootsOverride = GetRoots;
            DescribeSchemaTool.PlatformRootsOverride = GetRoots;
            QueryDatabaseTool.PlatformRootsOverride = GetRoots;
        }

        public void Dispose()
        {
            ListDatabasesTool.PlatformRootsOverride = previousListRootsOverride;
            DescribeSchemaTool.PlatformRootsOverride = previousDescribeRootsOverride;
            QueryDatabaseTool.PlatformRootsOverride = previousQueryRootsOverride;
        }
    }
}
