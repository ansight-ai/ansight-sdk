package ai.ansight.tools.database

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.ToolScope
import ai.ansight.runtime.androidDatabaseTool
import ai.ansight.runtime.intArg
import org.json.JSONArray
import org.json.JSONObject

object AndroidDatabaseTools {
    fun create(): List<AndroidTool> = listOf(
        androidDatabaseTool(
            DatabaseToolIds.ListDatabases,
            "List Databases",
            "Lists SQLite database files in app-owned roots.",
        ) { _, context ->
            val roots = AndroidDatabaseFileSandbox.roots(context.application)
            val databases = roots.values.flatMap { root ->
                root.walkTopDown().maxDepth(4).filter { it.isFile && AndroidSQLiteSupport.isDatabase(it) }.toList()
            }.distinctBy { it.canonicalPath }
            AndroidToolResult.success(
                JSONObject()
                    .put("databases", JSONArray(databases.map { AndroidDatabaseFileSandbox.describePath(context.application, it) }))
                    .put("count", databases.size),
            )
        },
        androidDatabaseTool(
            DatabaseToolIds.DescribeSchema,
            "Describe Schema",
            "Describes a SQLite database schema.",
        ) { args, context ->
            val db = AndroidDatabaseFileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            AndroidSQLiteSupport.openReadOnly(db.file).use { database ->
                AndroidToolResult.success(AndroidDatabaseFileSandbox.describe(db).put("tables", AndroidSQLiteSupport.tables(database)))
            }
        },
        androidDatabaseTool(
            DatabaseToolIds.Query,
            "Query Database",
            "Runs a read-only SQLite query.",
            ToolScope.Read,
        ) { args, context ->
            val sql = args["sql"]?.trim() ?: return@androidDatabaseTool AndroidToolResult.failure("SQL query is required.", "sql_required")
            if (!AndroidSQLiteSupport.isReadOnly(sql)) {
                return@androidDatabaseTool AndroidToolResult.failure("Only read-only SQLite queries are supported.", "sql_not_read_only")
            }
            val db = AndroidDatabaseFileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            AndroidSQLiteSupport.openReadOnly(db.file).use { database ->
                AndroidToolResult.success(
                    AndroidDatabaseFileSandbox.describe(db).put("query", AndroidSQLiteSupport.query(database, sql, args.intArg("limit", 100))),
                )
            }
        },
    )
}
