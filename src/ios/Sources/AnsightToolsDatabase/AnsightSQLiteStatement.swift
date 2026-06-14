import Foundation
import SQLite3

internal final class AnsightSQLiteStatement {
    private let database: AnsightSQLiteDatabase
    let handle: OpaquePointer

    init(database: AnsightSQLiteDatabase, sql: String) throws {
        self.database = database

        var preparedStatement: OpaquePointer?
        var remainingSQL = ""
        let prepareResult = sql.withCString { sqlPointer in
            var tail: UnsafePointer<CChar>?
            let result = sqlite3_prepare_v2(
                database.handle,
                sqlPointer,
                -1,
                &preparedStatement,
                &tail
            )

            if result == SQLITE_OK, let tail {
                remainingSQL = String(cString: tail)
                    .trimmingCharacters(in: .whitespacesAndNewlines)
            }

            return result
        }

        guard prepareResult == SQLITE_OK, let handle = preparedStatement else {
            if let preparedStatement {
                sqlite3_finalize(preparedStatement)
            }

            throw AnsightDatabaseToolError.operationFailed(
                "Failed to prepare SQLite statement: \(database.errorMessage)"
            )
        }

        guard remainingSQL.isEmpty else {
            sqlite3_finalize(handle)
            throw AnsightDatabaseToolError.invalidArgument(
                "Only a single read-only SQLite statement is supported."
            )
        }

        self.handle = handle
    }

    deinit {
        sqlite3_finalize(handle)
    }

    var columnCount: Int {
        Int(sqlite3_column_count(handle))
    }

    var isReadOnly: Bool {
        sqlite3_stmt_readonly(handle) != 0
    }

    func step() -> Int32 {
        sqlite3_step(handle)
    }

    func columnName(at index: Int) -> String {
        guard let pointer = sqlite3_column_name(handle, Int32(index)) else {
            return "column_\(index)"
        }

        let name = String(cString: pointer)
        return name.isEmpty ? "column_\(index)" : name
    }

    func columnDeclaredType(at index: Int) -> String? {
        Self.string(from: sqlite3_column_decltype(handle, Int32(index)))
    }

    func sourceDatabase(at index: Int) -> String? {
        Self.string(from: sqlite3_column_database_name(handle, Int32(index)))
    }

    func sourceTable(at index: Int) -> String? {
        Self.string(from: sqlite3_column_table_name(handle, Int32(index)))
    }

    func sourceColumn(at index: Int) -> String? {
        Self.string(from: sqlite3_column_origin_name(handle, Int32(index)))
    }

    private static func string(from pointer: UnsafePointer<CChar>?) -> String? {
        guard let pointer else {
            return nil
        }

        return String(cString: pointer)
    }
}
