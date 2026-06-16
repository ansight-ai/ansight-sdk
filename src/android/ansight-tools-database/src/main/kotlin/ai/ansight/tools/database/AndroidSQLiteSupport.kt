package ai.ansight.tools.database

import ai.ansight.runtime.putNullable

import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.Locale

object AndroidSQLiteSupport {
    fun isDatabase(file: File): Boolean {
        if (!file.isFile || file.length() < 16) {
            return false
        }
        return runCatching {
            file.inputStream().use { stream ->
                val header = ByteArray(16)
                stream.read(header) == 16 && String(header, Charsets.US_ASCII).startsWith("SQLite format 3")
            }
        }.getOrDefault(false)
    }

    fun openReadOnly(file: File): SQLiteDatabase = SQLiteDatabase.openDatabase(
        file.path,
        null,
        SQLiteDatabase.OPEN_READONLY or SQLiteDatabase.NO_LOCALIZED_COLLATORS,
    )

    fun tables(database: SQLiteDatabase): JSONArray {
        val cursor = database.rawQuery(
            "select name, type, sql from sqlite_master where type in ('table','view','index','trigger') order by type, name",
            emptyArray(),
        )
        return cursor.useRows { row ->
            JSONObject()
                .put("name", row.getString("name"))
                .put("type", row.getString("type"))
                .putNullable("sql", row.getString("sql"))
        }
    }

    fun query(database: SQLiteDatabase, sql: String, limit: Int): JSONObject {
        val limitedSql = sql.trim().trimEnd(';')
        val cursor = database.rawQuery("$limitedSql limit ${limit.coerceIn(1, 500)}", emptyArray())
        val rows = cursor.useRows { row ->
            val json = JSONObject()
            row.columns.forEach { column -> json.putNullable(column, row.value(column)) }
            json
        }
        return JSONObject().put("rows", rows).put("count", rows.length())
    }

    fun isReadOnly(sql: String): Boolean {
        val normalized = sql.trim().lowercase(Locale.US)
        return normalized.startsWith("select ") ||
            normalized.startsWith("pragma ") ||
            normalized.startsWith("with ") ||
            normalized.startsWith("explain ")
    }

    private fun Cursor.useRows(factory: (CursorRow) -> JSONObject): JSONArray {
        val rows = JSONArray()
        use {
            while (moveToNext()) {
                rows.put(factory(CursorRow(this)))
            }
        }
        return rows
    }

    private class CursorRow(private val cursor: Cursor) {
        val columns: List<String> = cursor.columnNames.toList()

        fun getString(column: String): String? = value(column)?.toString()

        fun value(column: String): Any? {
            val index = cursor.getColumnIndex(column)
            if (index < 0 || cursor.isNull(index)) {
                return null
            }
            return when (cursor.getType(index)) {
                Cursor.FIELD_TYPE_INTEGER -> cursor.getLong(index)
                Cursor.FIELD_TYPE_FLOAT -> cursor.getDouble(index)
                Cursor.FIELD_TYPE_BLOB -> Base64.encodeToString(cursor.getBlob(index), Base64.NO_WRAP)
                else -> cursor.getString(index)
            }
        }
    }
}
