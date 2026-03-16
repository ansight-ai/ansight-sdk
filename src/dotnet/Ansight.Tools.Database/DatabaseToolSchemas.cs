namespace Ansight.Tools.Database;

using Ansight.Tools;

internal static class DatabaseToolSchemas
{
    private static readonly ToolSchema GenericObjectSchema = ToolSchema.Object(
        description: "Arbitrary object with implementation-specific fields.",
        additionalProperties: true);

    private static readonly ToolSchema DatabaseEntrySchema = ToolSchema.Object(
        description: "Discovered SQLite database entry.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["name"] = ToolSchema.String("Database file name."),
            ["path"] = ToolSchema.String("Absolute database path."),
            ["relativePath"] = ToolSchema.String("Path relative to the sandbox root."),
            ["rootAlias"] = ToolSchema.String("Sandbox root alias."),
            ["sizeBytes"] = ToolSchema.Integer("Database file size."),
            ["lastModifiedUtc"] = ToolSchema.String("Last modification time.", format: "date-time")
        },
        required: new[] { "name", "path", "relativePath", "rootAlias", "sizeBytes", "lastModifiedUtc" });

    private static readonly ToolSchema TableSchema = ToolSchema.Object(
        description: "Database table or view metadata.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["name"] = ToolSchema.String("Table or view name."),
            ["type"] = ToolSchema.String("SQLite object type.", nullable: true),
            ["sql"] = ToolSchema.String("Create statement.", nullable: true),
            ["columns"] = ToolSchema.Array(GenericObjectSchema, "SQLite pragma column metadata."),
            ["indexes"] = ToolSchema.Array(GenericObjectSchema, "SQLite pragma index metadata.")
        },
        required: new[] { "name", "columns", "indexes" });

    internal static ToolSchema ListDatabasesArguments { get; } = ToolSchema.Object(
        description: "Arguments for discovering SQLite databases in the app sandbox.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["includeSystemStores"] = ToolSchema.Boolean("Include cache/system store databases."),
            ["maxResults"] = ToolSchema.Integer("Maximum number of database entries to return.")
        });

    internal static ToolSchema ListDatabasesResult { get; } = ToolSchema.Object(
        description: "Database discovery payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["databases"] = ToolSchema.Array(DatabaseEntrySchema, "Discovered databases."),
            ["truncated"] = ToolSchema.Boolean("Whether additional results were omitted."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "databases", "truncated", "capturedAtUtc" });

    internal static ToolSchema DescribeSchemaArguments { get; } = ToolSchema.Object(
        description: "Arguments for describing a database schema.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["path"] = ToolSchema.String("Absolute or sandbox-relative database path."),
            ["database"] = ToolSchema.String("Alternate field for the database path.", nullable: true),
            ["table"] = ToolSchema.String("Optional table name filter.", nullable: true)
        },
        required: new[] { "path" });

    internal static ToolSchema DescribeSchemaResult { get; } = ToolSchema.Object(
        description: "Database schema description payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["databasePath"] = ToolSchema.String("Resolved database path."),
            ["tables"] = ToolSchema.Array(TableSchema, "Table and view definitions."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "databasePath", "tables", "capturedAtUtc" });

    internal static ToolSchema QueryArguments { get; } = ToolSchema.Object(
        description: "Arguments for executing a read-only SQL query.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["path"] = ToolSchema.String("Absolute or sandbox-relative database path."),
            ["database"] = ToolSchema.String("Alternate field for the database path.", nullable: true),
            ["sql"] = ToolSchema.String("Read-only SQL statement."),
            ["maxRows"] = ToolSchema.Integer("Maximum number of rows to return.")
        },
        required: new[] { "path", "sql" });

    internal static ToolSchema QueryResult { get; } = ToolSchema.Object(
        description: "Read-only SQL query result payload.",
        properties: new Dictionary<string, ToolSchema>
        {
            ["databasePath"] = ToolSchema.String("Resolved database path."),
            ["sql"] = ToolSchema.String("Executed SQL."),
            ["columns"] = ToolSchema.Array(ToolSchema.String("Column name."), "Column names in result order."),
            ["rows"] = ToolSchema.Array(GenericObjectSchema, "Row values keyed by column name."),
            ["truncated"] = ToolSchema.Boolean("Whether additional rows were omitted."),
            ["capturedAtUtc"] = ToolSchema.String("UTC timestamp for capture.", format: "date-time")
        },
        required: new[] { "databasePath", "sql", "columns", "rows", "truncated", "capturedAtUtc" });
}
