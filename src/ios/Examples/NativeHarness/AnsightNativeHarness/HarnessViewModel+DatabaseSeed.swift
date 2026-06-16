import Foundation
import SQLite3

extension HarnessViewModel {
    func prepareHarnessDatabaseSample() throws {
        guard let directory = harnessDirectoryURL() else {
            throw harnessError("Unable to resolve the app Documents directory.")
        }

        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let database = directory.appendingPathComponent("sample.sqlite")

        var handle: OpaquePointer?
        let openResult = sqlite3_open_v2(
            database.path,
            &handle,
            SQLITE_OPEN_CREATE | SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX,
            nil
        )
        guard openResult == SQLITE_OK, let handle else {
            throw harnessError("Unable to create harness SQLite sample: \(sqliteError(handle))")
        }
        defer {
            sqlite3_close_v2(handle)
        }

        try createHarnessDatabaseTables(in: handle)
        try clearHarnessDatabaseTables(in: handle)
        try insertHarnessDatabaseRows(in: handle)
        databaseRowCount = try countRows(in: handle)
    }

    func createHarnessDatabaseTables(in handle: OpaquePointer) throws {
        try executeSQLite(handle, """
        CREATE TABLE IF NOT EXISTS harness_events (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            count INTEGER NOT NULL,
            recorded_at TEXT NOT NULL,
            payload BLOB
        );
        """)
        try executeSQLite(handle, """
        CREATE TABLE IF NOT EXISTS harness_orders (
            id INTEGER PRIMARY KEY,
            customer TEXT NOT NULL,
            status TEXT NOT NULL,
            total REAL NOT NULL,
            shipping_speed TEXT NOT NULL
        );
        """)
        try executeSQLite(handle, """
        CREATE TABLE IF NOT EXISTS harness_inventory (
            sku TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            stock INTEGER NOT NULL,
            warehouse TEXT NOT NULL
        );
        """)
        try executeSQLite(handle, """
        CREATE TABLE IF NOT EXISTS harness_navigation_events (
            id INTEGER PRIMARY KEY,
            event_name TEXT NOT NULL,
            recorded_at TEXT NOT NULL
        );
        """)
    }

    func clearHarnessDatabaseTables(in handle: OpaquePointer) throws {
        try executeSQLite(handle, "DELETE FROM harness_events;")
        try executeSQLite(handle, "DELETE FROM harness_orders;")
        try executeSQLite(handle, "DELETE FROM harness_inventory;")
        try executeSQLite(handle, "DELETE FROM harness_navigation_events;")
    }

    func insertHarnessDatabaseRows(in handle: OpaquePointer) throws {
        try executeSQLite(handle, """
        INSERT INTO harness_events (name, count, recorded_at, payload)
        VALUES
            ('startup', 1, '\(seededAtUtc)', X'000102FF'),
            ('screen_capture', 2, '\(seededAtUtc)', NULL),
            ('touch_capture', 3, '\(seededAtUtc)', NULL),
            ('picker_overlay', 4, '\(seededAtUtc)', NULL),
            ('inline_3d_viewer', 5, '\(seededAtUtc)', NULL);
        """)
        try executeSQLite(handle, """
        INSERT INTO harness_orders (customer, status, total, shipping_speed)
        VALUES
            ('Avery Stone', 'Draft', 128.40, 'Express'),
            ('Morgan Park', 'Paid', 349.99, 'Priority'),
            ('Riley Chen', 'Fulfillment', 74.25, 'Standard');
        """)
        try executeSQLite(handle, """
        INSERT INTO harness_inventory (sku, display_name, stock, warehouse)
        VALUES
            ('ANS-3D-CUBE', '3D Viewer Cube', 12, 'SYD'),
            ('ANS-PICKER', 'Picker Overlay Fixture', 7, 'MEL'),
            ('ANS-NAV-KIT', 'Navigation Harness Kit', 19, 'BNE');
        """)
        for event in navigationEvents.suffix(6) {
            try executeSQLite(handle, """
            INSERT INTO harness_navigation_events (event_name, recorded_at)
            VALUES ('\(escapedSQL(event))', '\(seededAtUtc)');
            """)
        }
    }
}
