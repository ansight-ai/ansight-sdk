import Foundation
import SQLite3

internal final class AnsightSQLiteDatabase {
    let path: String
    let handle: OpaquePointer

    init(path: String) throws {
        self.path = path

        var openedHandle: OpaquePointer?
        let result = sqlite3_open_v2(
            path,
            &openedHandle,
            SQLITE_OPEN_READONLY | SQLITE_OPEN_FULLMUTEX,
            nil
        )

        guard result == SQLITE_OK, let handle = openedHandle else {
            let message = Self.errorMessage(for: openedHandle)
            if let openedHandle {
                sqlite3_close_v2(openedHandle)
            }

            throw AnsightDatabaseToolError.operationFailed(
                "Unable to open SQLite database '\(path)': \(message)"
            )
        }

        self.handle = handle
        sqlite3_busy_timeout(handle, 500)
    }

    deinit {
        sqlite3_close_v2(handle)
    }

    var errorMessage: String {
        Self.errorMessage(for: handle)
    }

    static func errorMessage(for handle: OpaquePointer?) -> String {
        guard let handle, let pointer = sqlite3_errmsg(handle) else {
            return "unknown SQLite error"
        }

        return String(cString: pointer)
    }
}
