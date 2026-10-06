import Foundation
import SQLite3

extension HarnessViewModel {
    func executeSQLite(_ handle: OpaquePointer, _ sql: String) throws {
        var errorPointer: UnsafeMutablePointer<CChar>?
        let result = sqlite3_exec(handle, sql, nil, nil, &errorPointer)
        if result != SQLITE_OK {
            let message = errorPointer.map { String(cString: $0) } ?? sqliteError(handle)
            if let errorPointer {
                sqlite3_free(errorPointer)
            }

            throw harnessError("Harness SQLite statement failed: \(message)")
        }
    }

    func countRows(in handle: OpaquePointer) throws -> Int {
        let sql = """
        SELECT
            (SELECT COUNT(*) FROM harness_events) +
            (SELECT COUNT(*) FROM harness_orders) +
            (SELECT COUNT(*) FROM harness_inventory) +
            (SELECT COUNT(*) FROM harness_navigation_events);
        """
        var statement: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &statement, nil) == SQLITE_OK, let statement else {
            throw harnessError("Unable to count harness database rows: \(sqliteError(handle))")
        }
        defer {
            sqlite3_finalize(statement)
        }

        guard sqlite3_step(statement) == SQLITE_ROW else {
            return 0
        }

        return Int(sqlite3_column_int(statement, 0))
    }

    func sqliteError(_ handle: OpaquePointer?) -> String {
        guard let handle, let pointer = sqlite3_errmsg(handle) else {
            return "unknown SQLite error"
        }

        return String(cString: pointer)
    }

    func escapedSQL(_ value: String) -> String {
        value.replacingOccurrences(of: "'", with: "''")
    }
}
