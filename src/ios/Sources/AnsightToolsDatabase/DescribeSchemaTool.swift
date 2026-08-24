import AnsightCore
import Foundation

public final class DescribeSchemaTool: AnsightTool {
    private let options: AnsightDatabaseToolsOptions

    public init(options: AnsightDatabaseToolsOptions = .default) {
        self.options = options
    }

    public var descriptor: AnsightToolDescriptor {
        AnsightToolDescriptor(
            id: AnsightDatabaseToolIds.describeSchema,
            name: "Describe Schema",
            description: "Returns schema metadata for a database or table.",
            category: "data",
            policy: .read,
            keywords: "database schema tables columns sqlite",
            argumentsSchema: AnsightDatabaseToolSchemas.describeSchemaArguments,
            resultSchema: AnsightDatabaseToolSchemas.describeSchemaResult
        )
    }

    public func execute(arguments: [String: String]) throws -> AnsightToolExecutionResult {
        do {
            let roots = try AnsightDatabaseSandbox.roots(options: options)
            let resolvedDatabase = try AnsightDatabaseSandbox.resolveDatabasePath(arguments: arguments, roots: roots)
            let tableFilter = AnsightDatabaseArgumentReader.string(arguments, key: "table")
            let database = try AnsightSQLiteDatabase(path: resolvedDatabase.fullPath)
            let tables = try describeSchema(database: database, tableFilter: tableFilter)

            return .success(.object([
                "databasePath": .string(resolvedDatabase.fullPath),
                "tables": .array(tables),
                "capturedAtUtc": .string(AnsightClock.isoNow()),
            ]))
        } catch {
            return .failure(error.localizedDescription, errorCode: "database_schema_failed")
        }
    }

    private func describeSchema(database: AnsightSQLiteDatabase, tableFilter: String?) throws -> [JSONValue] {
        let tableResult = try AnsightSQLiteReadOnlyExecutor.execute(
            database: database,
            sql: """
            SELECT name, type, sql
            FROM sqlite_master
            WHERE type IN ('table', 'view')
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            """,
            maxRows: 512
        )

        var tableDefinitions: [JSONValue] = []
        for row in tableResult.rows {
            guard let name = row["name"]?.stringValue,
                  !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            else {
                continue
            }

            if let tableFilter,
               name.caseInsensitiveCompare(tableFilter) != .orderedSame {
                continue
            }

            let escapedName = Self.escapeSQLLiteral(name)
            let columns = try AnsightSQLiteReadOnlyExecutor.execute(
                database: database,
                sql: "PRAGMA table_xinfo('\(escapedName)')",
                maxRows: 512
            )
            let indexes = try AnsightSQLiteReadOnlyExecutor.execute(
                database: database,
                sql: "PRAGMA index_list('\(escapedName)')",
                maxRows: 512
            )

            tableDefinitions.append(.object([
                "name": .string(name),
                "type": row["type"] ?? .null,
                "sql": row["sql"] ?? .null,
                "columns": .array(columns.rows.map(JSONValue.object)),
                "indexes": .array(indexes.rows.map(JSONValue.object)),
            ]))
        }

        return tableDefinitions
    }

    private static func escapeSQLLiteral(_ value: String) -> String {
        value.replacingOccurrences(of: "'", with: "''")
    }
}
