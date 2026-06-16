import AnsightCore
import Foundation

public final class QueryDatabaseTool: AnsightTool {
    private let options: AnsightDatabaseToolsOptions

    public init(options: AnsightDatabaseToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightDatabaseToolIds.query,
            name: "Query Database",
            description: "Executes a constrained read query against an app database.",
            category: "data",
            scope: AnsightToolScope.read.rawValue,
            keywords: "database sql query read",
            security: AnsightDatabaseToolSecurityProfiles.query,
            argumentsSchema: AnsightDatabaseToolSchemas.queryArguments,
            resultSchema: AnsightDatabaseToolSchemas.queryResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let roots = try AnsightDatabaseSandbox.roots(options: options)
            let resolvedDatabase = try AnsightDatabaseSandbox.resolveDatabasePath(arguments: arguments, roots: roots)
            let sql = try AnsightDatabaseArgumentReader.requiredString(arguments, key: "sql")
            let maxRows = try AnsightDatabaseArgumentReader.integer(
                arguments,
                key: "maxRows",
                defaultValue: 100,
                minimum: 1,
                maximum: 1_000
            )

            let database = try AnsightSQLiteDatabase(path: resolvedDatabase.fullPath)
            let queryResult = try AnsightSQLiteReadOnlyExecutor.execute(
                database: database,
                sql: sql,
                maxRows: maxRows
            )

            return .success(.object([
                "databasePath": .string(resolvedDatabase.fullPath),
                "sql": .string(sql),
                "columns": .array(queryResult.columns.map(JSONValue.string)),
                "columnMetadata": .array(queryResult.columnMetadata.map(\.jsonValue)),
                "rows": .array(queryResult.rows.map(JSONValue.object)),
                "rowValues": .array(queryResult.rowValues.map(JSONValue.array)),
                "truncated": .bool(queryResult.truncated),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
            ]))
        } catch {
            return .failure(error.localizedDescription, errorCode: "database_query_failed")
        }
    }
}
