import SQLite3
import XCTest
@testable import AnsightCore
@testable import AnsightToolsDatabase

final class DatabaseToolTests: XCTestCase {
    func testDatabaseToolsRegisterExpectedToolIds() {
        let tools = AnsightDatabaseTools.tools()
        XCTAssertEqual(
            tools.map(\.descriptor.id),
            [
                AnsightDatabaseToolIds.listDatabases,
                AnsightDatabaseToolIds.describeSchema,
                AnsightDatabaseToolIds.query,
            ]
        )
    }

    func testListDatabasesFindsSQLiteFilesAndSkipsNonSQLiteFiles() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let fakeDatabase = root.appendingPathComponent("fake.db")
        try Data("not a sqlite database".utf8).write(to: fakeDatabase)
        let database = try createDatabase(
            root: root,
            relativePath: "sandbox/app.sqlite",
            statements: [
                "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);",
            ]
        )

        let envelope = try call(
            bridge(options: options(root: root), guardPolicy: .fullAccess),
            id: "database_list",
            toolId: AnsightDatabaseToolIds.listDatabases,
            arguments: ["maxResults": "10"]
        )
        let payload = try resultPayload(envelope)
        guard case .array(let databases)? = payload["databases"] else {
            return XCTFail("Expected database entries.")
        }

        let entries = databases.compactMap { value -> [String: JSONValue]? in
            guard case .object(let object) = value else {
                return nil
            }

            return object
        }
        let paths = entries.compactMap { $0["path"]?.stringValue }
        XCTAssertTrue(paths.contains(database.path))
        XCTAssertFalse(paths.contains(fakeDatabase.path))

        let entry = try XCTUnwrap(entries.first { $0["path"] == .string(database.path) })
        XCTAssertEqual(entry["name"], .string("app.sqlite"))
        XCTAssertEqual(entry["rootAlias"], .string("test"))
        XCTAssertEqual(entry["relativePath"], .string("sandbox/app.sqlite"))
        XCTAssertEqual(payload["truncated"], .bool(false))
    }

    func testListDatabasesSkipsCacheStoresUnlessIncluded() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let database = try createDatabase(
            root: root,
            relativePath: "Library/Caches/cache.sqlite",
            statements: [
                "CREATE TABLE cache_entries (id INTEGER PRIMARY KEY, value TEXT);",
            ]
        )
        let bridge = bridge(options: options(root: root), guardPolicy: .fullAccess)

        let defaultPayload = try resultPayload(try call(
            bridge,
            id: "database_list_cache_default",
            toolId: AnsightDatabaseToolIds.listDatabases,
            arguments: [:]
        ))
        XCTAssertFalse(try databasePaths(defaultPayload).contains(database.path))

        let includedPayload = try resultPayload(try call(
            bridge,
            id: "database_list_cache_included",
            toolId: AnsightDatabaseToolIds.listDatabases,
            arguments: ["includeSystemStores": "true"]
        ))
        XCTAssertTrue(try databasePaths(includedPayload).contains(database.path))
    }

    func testDescribeSchemaUsesDatabaseAliasAndAppliesTableFilter() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let database = try createDatabase(
            root: root,
            relativePath: "app.sqlite",
            statements: [
                "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER);",
                "CREATE TABLE audit_log (id INTEGER PRIMARY KEY, action TEXT NOT NULL);",
            ]
        )

        let payload = try resultPayload(try call(
            bridge(options: options(root: root), guardPolicy: .fullAccess),
            id: "database_schema",
            toolId: AnsightDatabaseToolIds.describeSchema,
            arguments: [
                "database": database.path,
                "table": "users",
            ]
        ))
        XCTAssertEqual(payload["databasePath"], .string(database.path))

        guard case .array(let tables)? = payload["tables"],
              tables.count == 1,
              case .object(let table) = tables[0],
              case .array(let columns)? = table["columns"] else {
            return XCTFail("Expected one schema table.")
        }

        XCTAssertEqual(table["name"], .string("users"))
        let columnNames = columns.compactMap { value -> String? in
            guard case .object(let column) = value else {
                return nil
            }

            return column["name"]?.stringValue
        }
        XCTAssertEqual(columnNames, ["id", "name", "age"])
    }

    func testQueryReturnsRowsMetadataTypedStorageValuesAndTruncation() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let database = try createDatabase(
            root: root,
            relativePath: "app.sqlite",
            statements: [
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
                """,
                "INSERT INTO samples (integer_value, text_value) VALUES (43, 'second');",
                "INSERT INTO samples (integer_value, text_value) VALUES (44, 'third');",
            ]
        )
        let bridge = bridge(options: options(root: root), guardPolicy: .fullAccess)

        let typedPayload = try resultPayload(try call(
            bridge,
            id: "database_query_typed",
            toolId: AnsightDatabaseToolIds.query,
            arguments: [
                "path": database.path,
                "sql": """
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
                    WHERE id = 1
                    """,
            ]
        ))

        guard case .array(let metadataValues)? = typedPayload["columnMetadata"],
              case .array(let rowValues)? = typedPayload["rowValues"],
              case .array(let rows)? = typedPayload["rows"],
              case .object(let row) = rows.first else {
            return XCTFail("Expected typed query result.")
        }

        let metadata = metadataValues.compactMap { value -> [String: JSONValue]? in
            guard case .object(let object) = value else {
                return nil
            }

            return object
        }
        XCTAssertEqual(metadata[0]["key"], .string("integer_value"))
        XCTAssertEqual(metadata[0]["declaredType"], .string("INTEGER"))
        XCTAssertEqual(metadata[5]["declaredType"], .string("BOOLEAN"))
        XCTAssertEqual(metadata[6]["declaredType"], .string("DATETIME"))
        XCTAssertEqual(metadata[7]["declaredType"], .string("UNIQUEIDENTIFIER"))
        XCTAssertEqual(metadata[8]["declaredType"], .string("DECIMAL(18,2)"))
        XCTAssertEqual(metadata[9]["declaredType"], .string("JSON"))

        XCTAssertEqual(row["integer_value"], .integer(42))
        XCTAssertEqual(row["real_value"], .number(3.25))
        XCTAssertEqual(row["text_value"], .string("hello"))
        XCTAssertEqual(row["null_value"], .null)
        XCTAssertEqual(row["bool_value"], .integer(1))
        XCTAssertEqual(row["date_value"], .string("2026-04-21T01:02:03Z"))
        XCTAssertEqual(row["guid_value"], .string("01234567-89ab-cdef-0123-456789abcdef"))
        XCTAssertEqual(row["decimal_value"], .number(1234.56))
        XCTAssertEqual(row["json_value"], .string("{\"ok\":true}"))
        XCTAssertEqual(row["blob_value"], .object([
            "type": .string("blob"),
            "base64": .string("AAEC/w=="),
            "byteLength": .integer(4),
        ]))

        guard case .array(let cells)? = rowValues.first else {
            return XCTFail("Expected row value cells.")
        }
        let storageTypes = cells.compactMap { value -> String? in
            guard case .object(let cell) = value else {
                return nil
            }

            return cell["storageType"]?.stringValue
        }
        XCTAssertEqual(
            Array(storageTypes.prefix(8)),
            ["integer", "real", "text", "blob", "null", "integer", "text", "text"]
        )

        let truncatedPayload = try resultPayload(try call(
            bridge,
            id: "database_query_truncated",
            toolId: AnsightDatabaseToolIds.query,
            arguments: [
                "path": database.path,
                "sql": "SELECT id, integer_value FROM samples ORDER BY id",
                "maxRows": "2",
            ]
        ))
        guard case .array(let truncatedRows)? = truncatedPayload["rows"] else {
            return XCTFail("Expected truncated query rows.")
        }
        XCTAssertEqual(truncatedRows.count, 2)
        XCTAssertEqual(truncatedPayload["truncated"], .bool(true))
    }

    func testQueryPreservesDuplicateColumnKeysAndEmbeddedNullText() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let database = try createDatabase(
            root: root,
            relativePath: "app.sqlite",
            statements: [
                "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);",
                "CREATE TABLE orders (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL);",
                "CREATE TABLE samples (value TEXT);",
                "INSERT INTO users (id, name) VALUES (1, 'Ada');",
                "INSERT INTO orders (id, user_id) VALUES (10, 1);",
                "INSERT INTO samples (value) VALUES ('a' || char(0) || 'b');",
            ]
        )
        let bridge = bridge(options: options(root: root), guardPolicy: .fullAccess)

        let duplicatePayload = try resultPayload(try call(
            bridge,
            id: "database_query_duplicate_columns",
            toolId: AnsightDatabaseToolIds.query,
            arguments: [
                "path": database.path,
                "sql": """
                    SELECT users.id, orders.id, users.name
                    FROM users
                    INNER JOIN orders ON orders.user_id = users.id
                    """,
            ]
        ))
        guard case .array(let columnValues)? = duplicatePayload["columns"],
              case .array(let metadataValues)? = duplicatePayload["columnMetadata"],
              case .array(let rows)? = duplicatePayload["rows"],
              case .object(let row) = rows.first else {
            return XCTFail("Expected duplicate column query result.")
        }

        XCTAssertEqual(columnValues.map(\.stringValue), ["id", "id", "name"])
        XCTAssertEqual(metadataValues.compactMap { value -> String? in
            guard case .object(let metadata) = value else {
                return nil
            }

            return metadata["key"]?.stringValue
        }, ["id", "id_2", "name"])
        XCTAssertEqual(row["id"], .integer(1))
        XCTAssertEqual(row["id_2"], .integer(10))
        XCTAssertEqual(row["name"], .string("Ada"))

        let embeddedNullPayload = try resultPayload(try call(
            bridge,
            id: "database_query_embedded_null",
            toolId: AnsightDatabaseToolIds.query,
            arguments: [
                "path": database.path,
                "sql": "SELECT value FROM samples",
            ]
        ))
        guard case .array(let nullRows)? = embeddedNullPayload["rows"],
              case .object(let nullRow) = nullRows.first,
              case .string(let value)? = nullRow["value"] else {
            return XCTFail("Expected embedded null text value.")
        }

        XCTAssertEqual(value.count, 3)
        XCTAssertEqual(value, "a\0b")
    }

    func testQueryRejectsWriteStatementsAndMultipleStatements() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let database = try createDatabase(
            root: root,
            relativePath: "app.sqlite",
            statements: [
                "CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT NOT NULL);",
                "INSERT INTO users (name) VALUES ('Ada');",
            ]
        )
        let bridge = bridge(options: options(root: root), guardPolicy: .fullAccess)

        let writeEnvelope = try call(
            bridge,
            id: "database_write_denied",
            toolId: AnsightDatabaseToolIds.query,
            arguments: [
                "path": database.path,
                "sql": "DELETE FROM users",
            ]
        )
        XCTAssertEqual(writeEnvelope.type, "tool.error")
        XCTAssertEqual(errorCode(writeEnvelope), "database_query_failed")
        XCTAssertTrue(errorMessage(writeEnvelope)?.localizedCaseInsensitiveContains("read-only") == true)

        let multipleEnvelope = try call(
            bridge,
            id: "database_multiple_denied",
            toolId: AnsightDatabaseToolIds.query,
            arguments: [
                "path": database.path,
                "sql": "SELECT id, name FROM users ORDER BY id; SELECT COUNT(*) FROM users",
            ]
        )
        XCTAssertEqual(multipleEnvelope.type, "tool.error")
        XCTAssertEqual(errorCode(multipleEnvelope), "database_query_failed")
        XCTAssertTrue(errorMessage(multipleEnvelope)?.localizedCaseInsensitiveContains("single") == true)
    }

    func testDatabaseCatalogIncludesSecurityMetadata() throws {
        let root = try makeTemporaryRoot()
        defer {
            try? FileManager.default.removeItem(at: root)
        }

        let envelope = try queryCatalog(bridge(options: options(root: root), guardPolicy: .fullAccess))
        XCTAssertEqual(envelope.type, "tool.catalog")
        guard case .object(let payload) = envelope.payload,
              case .array(let tools)? = payload["tools"] else {
            return XCTFail("Expected catalog tools.")
        }

        let queryTool = tools.compactMap { value -> [String: JSONValue]? in
            guard case .object(let object) = value,
                  object["id"] == .string(AnsightDatabaseToolIds.query) else {
                return nil
            }

            return object
        }.first

        guard let queryTool,
              case .object(let security)? = queryTool["security"],
              case .array(let implications)? = security["implications"] else {
            return XCTFail("Expected query security metadata.")
        }

        XCTAssertEqual(security["level"], .string("High"))
        XCTAssertTrue(implications.contains(.string("reads_app_data")))
        XCTAssertTrue(implications.contains(.string("exports_data")))
        XCTAssertTrue(implications.contains(.string("accesses_databases")))
    }

    private func options(root: URL) -> AnsightDatabaseToolsOptions {
        AnsightDatabaseToolsOptions(
            additionalRoots: [
                AnsightDatabaseRoot(alias: "test", path: root.path),
            ],
            includePlatformRoots: false
        )
    }

    private func makeTemporaryRoot() throws -> URL {
        let root = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("ansight-database-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        return root
    }

    private func createDatabase(root: URL, relativePath: String, statements: [String]) throws -> URL {
        let database = root.appendingPathComponent(relativePath)
        try FileManager.default.createDirectory(at: database.deletingLastPathComponent(), withIntermediateDirectories: true)

        var handle: OpaquePointer?
        let openResult = sqlite3_open_v2(
            database.path,
            &handle,
            SQLITE_OPEN_CREATE | SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX,
            nil
        )
        guard openResult == SQLITE_OK, let handle else {
            throw XCTSkip("Failed to create SQLite database: \(sqliteError(handle))")
        }
        defer {
            sqlite3_close_v2(handle)
        }

        for statement in statements {
            var errorPointer: UnsafeMutablePointer<CChar>?
            let result = sqlite3_exec(handle, statement, nil, nil, &errorPointer)
            if result != SQLITE_OK {
                let message = errorPointer.map { String(cString: $0) } ?? sqliteError(handle)
                if let errorPointer {
                    sqlite3_free(errorPointer)
                }

                throw XCTSkip("Failed to execute SQLite statement: \(message)")
            }
        }

        return database
    }

    private func bridge(options: AnsightDatabaseToolsOptions, guardPolicy: AnsightToolGuard) -> AnsightToolProtocolBridge {
        let tools = AnsightDatabaseTools.tools(options: options)
        let registry = Dictionary(
            uniqueKeysWithValues: tools.map { tool in
                (
                    AnsightToolProtocolBridge.normalizedToolId(tool.descriptor.id),
                    RegisteredTool(
                        descriptor: tool.descriptor,
                        execute: { arguments in
                            try tool.execute(arguments: arguments)
                        }
                    )
                )
            }
        )

        return AnsightToolProtocolBridge(registry: registry, guardPolicy: guardPolicy)
    }

    private func queryCatalog(_ bridge: AnsightToolProtocolBridge) throws -> AnsightToolProtocolEnvelope {
        let responseJson = try bridge.handleIfSupported(
            """
            {"type":"tool.query","id":"database_catalog","capability":"tool.exec","payload":{}}
            """
        )
        return try decodeEnvelope(responseJson)
    }

    private func call(
        _ bridge: AnsightToolProtocolBridge,
        id: String,
        toolId: String,
        arguments: [String: String]
    ) throws -> AnsightToolProtocolEnvelope {
        let envelope = AnsightToolProtocolEnvelope(
            type: "tool.call",
            id: id,
            sessionId: "database_session",
            payload: .object([
                "toolId": .string(toolId),
                "arguments": .object(from: arguments),
            ])
        )
        let data = try JSONEncoder().encode(envelope)
        let request = try XCTUnwrap(String(data: data, encoding: .utf8))
        let responseJson = try bridge.handleIfSupported(request)
        return try decodeEnvelope(responseJson)
    }

    private func decodeEnvelope(_ json: String?) throws -> AnsightToolProtocolEnvelope {
        let json = try XCTUnwrap(json)
        let data = try XCTUnwrap(json.data(using: .utf8))
        return try JSONDecoder().decode(AnsightToolProtocolEnvelope.self, from: data)
    }

    private func resultPayload(_ envelope: AnsightToolProtocolEnvelope) throws -> [String: JSONValue] {
        guard case .object(let payload) = envelope.payload,
              case .object(let result)? = payload["result"] else {
            XCTFail("Expected tool result payload.")
            return [:]
        }

        return result
    }

    private func databasePaths(_ payload: [String: JSONValue]) throws -> [String] {
        guard case .array(let databases)? = payload["databases"] else {
            throw XCTSkip("Expected database entries.")
        }

        return databases.compactMap { value in
            guard case .object(let object) = value else {
                return nil
            }

            return object["path"]?.stringValue
        }
    }

    private func errorCode(_ envelope: AnsightToolProtocolEnvelope) -> String? {
        guard case .object(let payload) = envelope.payload,
              case .string(let code)? = payload["code"] else {
            return nil
        }

        return code
    }

    private func errorMessage(_ envelope: AnsightToolProtocolEnvelope) -> String? {
        guard case .object(let payload) = envelope.payload,
              case .string(let message)? = payload["message"] else {
            return nil
        }

        return message
    }

    private func sqliteError(_ handle: OpaquePointer?) -> String {
        guard let handle, let pointer = sqlite3_errmsg(handle) else {
            return "unknown SQLite error"
        }

        return String(cString: pointer)
    }
}
