import AnsightCore
import Foundation
import SQLite3

internal enum AnsightSQLiteReadOnlyExecutor {
    static func execute(
        database: AnsightSQLiteDatabase,
        sql: String,
        maxRows: Int
    ) throws -> AnsightSQLiteQueryResult {
        let statement = try AnsightSQLiteStatement(database: database, sql: sql)

        guard statement.isReadOnly else {
            throw AnsightDatabaseToolError.invalidArgument(
                "Only read-only SQLite statements are supported."
            )
        }

        let columnMetadata = readColumnMetadata(statement: statement)
        let columns = columnMetadata.map(\.name)
        var rows: [[String: JSONValue]] = []
        var rowValues: [[JSONValue]] = []
        var truncated = false

        while true {
            let stepResult = statement.step()
            if stepResult == SQLITE_DONE {
                break
            }

            guard stepResult == SQLITE_ROW else {
                throw AnsightDatabaseToolError.operationFailed(
                    "SQLite query execution failed: \(database.errorMessage)"
                )
            }

            if rows.count >= maxRows {
                truncated = true
                break
            }

            var rowObject: [String: JSONValue] = [:]
            var rowValueArray: [JSONValue] = []
            for column in columnMetadata {
                let cell = readColumnValue(statement: statement, index: column.index)
                rowObject[column.key] = cell.value
                rowValueArray.append(.object([
                    "columnKey": .string(column.key),
                    "columnName": .string(column.name),
                    "storageType": .string(cell.storageType),
                    "value": cell.value,
                ]))
            }

            rows.append(rowObject)
            rowValues.append(rowValueArray)
        }

        return AnsightSQLiteQueryResult(
            columns: columns,
            columnMetadata: columnMetadata,
            rows: rows,
            rowValues: rowValues,
            truncated: truncated
        )
    }

    private static func readColumnMetadata(statement: AnsightSQLiteStatement) -> [AnsightSQLiteColumnMetadata] {
        var columns: [AnsightSQLiteColumnMetadata] = []
        var usedKeys: Set<String> = []

        for index in 0..<statement.columnCount {
            let name = statement.columnName(at: index)
            let key = createUniqueColumnKey(columnName: name, columnIndex: index, usedKeys: &usedKeys)
            columns.append(AnsightSQLiteColumnMetadata(
                index: index,
                name: name,
                key: key,
                declaredType: statement.columnDeclaredType(at: index),
                sourceDatabase: statement.sourceDatabase(at: index),
                sourceTable: statement.sourceTable(at: index),
                sourceColumn: statement.sourceColumn(at: index)
            ))
        }

        return columns
    }

    private static func createUniqueColumnKey(
        columnName: String,
        columnIndex: Int,
        usedKeys: inout Set<String>
    ) -> String {
        let baseKey = columnName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            ? "column_\(columnIndex)"
            : columnName

        if usedKeys.insert(baseKey).inserted {
            return baseKey
        }

        var suffix = 2
        while true {
            let candidate = "\(baseKey)_\(suffix)"
            if usedKeys.insert(candidate).inserted {
                return candidate
            }

            suffix += 1
        }
    }

    private static func readColumnValue(statement: AnsightSQLiteStatement, index: Int) -> AnsightSQLiteCellValue {
        switch sqlite3_column_type(statement.handle, Int32(index)) {
        case SQLITE_INTEGER:
            return AnsightSQLiteCellValue(
                storageType: "integer",
                value: .integer(sqlite3_column_int64(statement.handle, Int32(index)))
            )
        case SQLITE_FLOAT:
            return AnsightSQLiteCellValue(
                storageType: "real",
                value: .number(sqlite3_column_double(statement.handle, Int32(index)))
            )
        case SQLITE_TEXT:
            return AnsightSQLiteCellValue(
                storageType: "text",
                value: readText(statement: statement, index: index)
            )
        case SQLITE_BLOB:
            return AnsightSQLiteCellValue(
                storageType: "blob",
                value: readBlob(statement: statement, index: index)
            )
        case SQLITE_NULL:
            return AnsightSQLiteCellValue(storageType: "null", value: .null)
        default:
            return AnsightSQLiteCellValue(storageType: "unknown", value: .null)
        }
    }

    private static func readText(statement: AnsightSQLiteStatement, index: Int) -> JSONValue {
        let byteCount = Int(sqlite3_column_bytes(statement.handle, Int32(index)))
        guard byteCount > 0, let pointer = sqlite3_column_text(statement.handle, Int32(index)) else {
            return .string("")
        }

        let buffer = UnsafeRawBufferPointer(start: pointer, count: byteCount)
        let data = Data(buffer)
        return .string(String(decoding: data, as: UTF8.self))
    }

    private static func readBlob(statement: AnsightSQLiteStatement, index: Int) -> JSONValue {
        let byteCount = Int(sqlite3_column_bytes(statement.handle, Int32(index)))
        guard byteCount > 0, let pointer = sqlite3_column_blob(statement.handle, Int32(index)) else {
            return .object([
                "type": .string("blob"),
                "base64": .string(""),
                "byteLength": .integer(0),
            ])
        }

        let data = Data(bytes: pointer, count: byteCount)
        return .object([
            "type": .string("blob"),
            "base64": .string(data.base64EncodedString()),
            "byteLength": .integer(Int64(byteCount)),
        ])
    }
}
