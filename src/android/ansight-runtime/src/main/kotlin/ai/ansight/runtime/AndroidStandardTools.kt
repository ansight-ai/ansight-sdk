package ai.ansight.runtime

import android.app.Application
import android.content.SharedPreferences
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.os.Build
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.nio.charset.Charset
import java.security.MessageDigest
import java.util.Locale

object AndroidStandardTools {
    fun create(): List<AndroidTool> {
        val tools = mutableListOf<AndroidTool>()
        tools.add(uiTool("ui.get_visual_tree", "Get Visual Tree", "Returns the current Android View hierarchy.") { _, _ ->
            AndroidToolResult.success(AndroidUiEvidence.visualTree())
        })
        tools.add(uiTool("ui.get_screenshot", "Get Screenshot", "Captures a screenshot of the current Android activity.") { args, context ->
            val screenshot = AndroidUiEvidence.captureScreenshot(
                format = args["format"] ?: "jpeg",
                quality = args.intArg("quality", context.options.sessionJpegCapture?.quality ?: 80),
                maxWidth = args["maxWidth"]?.toIntOrNull() ?: context.options.sessionJpegCapture?.maxWidth,
            )
            val transferId = PairingFileTransferWireProtocol.newTransferId()
            val downloadId = args["downloadId"]?.trim()?.ifBlank { null } ?: context.requestId ?: transferId
            val descriptor = BinaryTransferDescriptor(
                transferId = transferId,
                downloadId = downloadId,
                fileName = screenshot.fileName,
                mimeType = screenshot.mimeType,
                sizeBytes = screenshot.bytes.size.toLong(),
                chunkBytes = args.intArg("chunkBytes", 64 * 1024),
                status = if (context.transport?.isOpen == true) "started" else "unavailable",
            ).toJson()
                .put("width", screenshot.width)
                .put("height", screenshot.height)

            context.transport?.takeIf { it.isOpen }?.let { transport ->
                Thread {
                    transport.sendBinaryTransfer(transferId, screenshot.bytes, args.intArg("chunkBytes", 64 * 1024))
                }.apply {
                    name = "AnsightAndroidScreenshotTransfer"
                    isDaemon = true
                    start()
                }
            }
            AndroidToolResult.success(descriptor)
        })
        tools.add(uiTool("ui.inspect_node", "Inspect Node", "Returns one node from the current visual tree.") { args, _ ->
            val id = args["id"] ?: args["nodeId"] ?: return@uiTool AndroidToolResult.failure("Node id is required.", "node_id_required")
            AndroidToolResult.success(AndroidUiEvidence.inspectNode(id))
        })
        tools.add(uiTool("ui.show_overlay", "Show Overlay", "Shows a rectangular diagnostic overlay.", ToolScope.Write) { args, _ ->
            AndroidToolResult.success(AndroidUiEvidence.showOverlay(args))
        })
        tools.add(uiTool("ui.get_overlay", "Get Overlay", "Returns one diagnostic overlay.") { args, _ ->
            val id = args["id"] ?: return@uiTool AndroidToolResult.failure("Overlay id is required.", "overlay_id_required")
            AndroidToolResult.success(AndroidUiEvidence.getOverlay(id))
        })
        tools.add(uiTool("ui.query_overlays", "Query Overlays", "Lists active diagnostic overlays.") { _, _ ->
            AndroidToolResult.success(AndroidUiEvidence.queryOverlays())
        })
        tools.add(uiTool("ui.update_overlay", "Update Overlay", "Updates a diagnostic overlay.", ToolScope.Write) { args, _ ->
            AndroidToolResult.success(AndroidUiEvidence.updateOverlay(args))
        })
        tools.add(uiTool("ui.remove_overlay", "Remove Overlay", "Removes a diagnostic overlay.", ToolScope.Delete) { args, _ ->
            val id = args["id"] ?: return@uiTool AndroidToolResult.failure("Overlay id is required.", "overlay_id_required")
            AndroidToolResult.success(AndroidUiEvidence.removeOverlay(id))
        })
        tools.add(uiTool("ui.clear_overlays", "Clear Overlays", "Clears diagnostic overlays.", ToolScope.Delete) { _, _ ->
            AndroidToolResult.success(AndroidUiEvidence.clearOverlays())
        })

        tools.add(fileTool("files.list_directory", "List Directory", "Lists files inside an approved app root.") { args, context ->
            val directory = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = true)
            val entries = directory.file.listFiles()?.sortedBy { it.name.toLowerCase(Locale.US) } ?: emptyList()
            AndroidToolResult.success(
                FileSandbox.describe(directory).put(
                    "entries",
                    JSONArray(entries.map { child ->
                        JSONObject()
                            .put("name", child.name)
                            .put("path", directory.relativePath(child))
                            .put("isDirectory", child.isDirectory)
                            .put("sizeBytes", if (child.isFile) child.length() else JSONObject.NULL)
                            .put("lastModifiedEpochMs", child.lastModified())
                    }),
                ),
            )
        })
        tools.add(fileTool("files.read_file", "Read File", "Reads a UTF-8 text file inside an approved app root.") { args, context ->
            val file = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            val maxBytes = args.intArg("maxBytes", 256 * 1024).coerceIn(1, 1024 * 1024)
            val bytes = file.file.readBytes()
            if (bytes.size > maxBytes) {
                return@fileTool AndroidToolResult.failure("File exceeds maxBytes.", "file_too_large", FileSandbox.describe(file))
            }
            AndroidToolResult.success(
                FileSandbox.describe(file)
                    .put("text", bytes.toString(Charset.forName(args["encoding"] ?: "UTF-8")))
                    .put("encoding", args["encoding"] ?: "UTF-8"),
            )
        })
        tools.add(fileTool("files.get_file_checksum", "Get File Checksum", "Returns a SHA-256 checksum for a file.") { args, context ->
            val file = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            val digest = MessageDigest.getInstance("SHA-256").digest(file.file.readBytes()).joinToString("") { "%02x".format(it) }
            AndroidToolResult.success(FileSandbox.describe(file).put("sha256", digest))
        })
        tools.add(fileTool("files.download_file", "Download File", "Returns a small file inline as base64.") { args, context ->
            val file = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            val maxBytes = args.intArg("maxBytes", 512 * 1024).coerceIn(1, 1024 * 1024)
            val bytes = file.file.readBytes()
            if (bytes.size > maxBytes) {
                return@fileTool AndroidToolResult.failure("File exceeds maxBytes; use files.begin_binary_download.", "file_too_large", FileSandbox.describe(file))
            }
            AndroidToolResult.success(
                FileSandbox.describe(file)
                    .put("contentBase64", Base64.encodeToString(bytes, Base64.NO_WRAP))
                    .put("deliveryMode", "inline_base64"),
            )
        })
        tools.add(fileTool("files.begin_binary_download", "Begin Binary Download", "Transfers a file over the WebSocket binary channel.") { args, context ->
            val file = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            val transferId = args["transferId"]?.trim()?.ifBlank { null } ?: PairingFileTransferWireProtocol.newTransferId()
            val downloadId = args["downloadId"]?.trim()?.ifBlank { null } ?: context.requestId ?: transferId
            val chunkBytes = args.intArg("chunkBytes", 64 * 1024)
            val bytes = file.file.readBytes()
            val descriptor = FileSandbox.describe(file)
                .put("transfer", BinaryTransferDescriptor(
                    transferId = transferId,
                    downloadId = downloadId,
                    fileName = file.file.name,
                    mimeType = args["mimeType"] ?: "application/octet-stream",
                    sizeBytes = bytes.size.toLong(),
                    chunkBytes = chunkBytes,
                    status = if (context.transport?.isOpen == true) "started" else "unavailable",
                ).toJson())
            context.transport?.takeIf { it.isOpen }?.let { transport ->
                Thread {
                    transport.sendBinaryTransfer(transferId, bytes, chunkBytes)
                }.apply {
                    name = "AnsightAndroidFileTransfer"
                    isDaemon = true
                    start()
                }
            }
            AndroidToolResult.success(descriptor)
        })
        tools.add(fileTool("files.push_file", "Push File", "Writes a file inside an approved app root.", ToolScope.Write) { args, context ->
            val file = FileSandbox.resolve(context.application, args, requireExisting = false, expectDirectory = false)
            file.file.parentFile?.mkdirs()
            val bytes = args["contentBase64"]?.let { Base64.decode(it, Base64.DEFAULT) }
                ?: (args["content"] ?: "").toByteArray(Charsets.UTF_8)
            file.file.writeBytes(bytes)
            AndroidToolResult.success(FileSandbox.describe(file).put("writtenBytes", bytes.size))
        })
        tools.add(fileTool("files.copy_file", "Copy File", "Copies a file between approved app roots.", ToolScope.Write) { args, context ->
            val source = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            val destination = FileSandbox.resolve(context.application, args, pathKey = "destinationPath", rootKey = "destinationRoot", requireExisting = false, expectDirectory = false)
            destination.file.parentFile?.mkdirs()
            source.file.copyTo(destination.file, overwrite = args.booleanArg("overwrite", false))
            AndroidToolResult.success(FileSandbox.describe(destination))
        })
        tools.add(fileTool("files.move_file", "Move File", "Moves a file between approved app roots.", ToolScope.Write) { args, context ->
            val source = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            val destination = FileSandbox.resolve(context.application, args, pathKey = "destinationPath", rootKey = "destinationRoot", requireExisting = false, expectDirectory = false)
            destination.file.parentFile?.mkdirs()
            if (!source.file.renameTo(destination.file)) {
                source.file.copyTo(destination.file, overwrite = args.booleanArg("overwrite", false))
                source.file.delete()
            }
            AndroidToolResult.success(FileSandbox.describe(destination))
        })
        tools.add(fileTool("files.delete_file", "Delete File", "Deletes a file inside an approved app root.", ToolScope.Delete) { args, context ->
            val file = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            val deleted = file.file.delete()
            AndroidToolResult.success(FileSandbox.describe(file).put("deleted", deleted))
        })

        tools.add(prefsTool("prefs.list_keys", "List Preference Keys", "Lists keys from SharedPreferences.") { args, context ->
            val prefs = preferences(context.application, args)
            AndroidToolResult.success(JSONObject()
                .put("name", prefs.first)
                .put("keys", JSONArray(prefs.second.all.keys.sorted()))
                .put("count", prefs.second.all.size))
        })
        tools.add(prefsTool("prefs.get_value", "Get Preference Value", "Reads one SharedPreferences value.") { args, context ->
            val key = args["key"] ?: return@prefsTool AndroidToolResult.failure("Preference key is required.", "preference_key_required")
            val prefs = preferences(context.application, args)
            AndroidToolResult.success(JSONObject()
                .put("name", prefs.first)
                .put("key", key)
                .putNullable("value", prefs.second.all[key]?.toString())
                .put("exists", prefs.second.all.containsKey(key)))
        })
        tools.add(prefsTool("prefs.set_value", "Set Preference Value", "Writes one SharedPreferences string value.", ToolScope.Write) { args, context ->
            val key = args["key"] ?: return@prefsTool AndroidToolResult.failure("Preference key is required.", "preference_key_required")
            val value = args["value"] ?: ""
            val prefs = preferences(context.application, args)
            prefs.second.edit().putString(key, value).apply()
            AndroidToolResult.success(JSONObject().put("name", prefs.first).put("key", key).put("written", true))
        })
        tools.add(prefsTool("prefs.remove_key", "Remove Preference Key", "Removes one SharedPreferences key.", ToolScope.Delete) { args, context ->
            val key = args["key"] ?: return@prefsTool AndroidToolResult.failure("Preference key is required.", "preference_key_required")
            val prefs = preferences(context.application, args)
            prefs.second.edit().remove(key).apply()
            AndroidToolResult.success(JSONObject().put("name", prefs.first).put("key", key).put("removed", true))
        })

        tools.add(secureTool("secure.get_value", "Get Secure Storage Value", "Reads an explicitly allow-listed secure value.") { args, context ->
            val key = args["key"] ?: return@secureTool AndroidToolResult.failure("Secure storage key is required.", "secure_key_required")
            if (!context.options.secureStorage.isAllowed(key)) {
                return@secureTool AndroidToolResult.failure("Secure storage key is not allow-listed.", "secure_key_denied")
            }
            val prefs = context.application.getSharedPreferences(context.options.secureStorage.preferencesName, Application.MODE_PRIVATE)
            AndroidToolResult.success(JSONObject().put("key", key).putNullable("value", prefs.getString(key, null)).put("exists", prefs.contains(key)))
        })
        tools.add(secureTool("secure.set_value", "Set Secure Storage Value", "Writes an explicitly allow-listed secure value.", ToolScope.Write) { args, context ->
            val key = args["key"] ?: return@secureTool AndroidToolResult.failure("Secure storage key is required.", "secure_key_required")
            if (!context.options.secureStorage.isAllowed(key)) {
                return@secureTool AndroidToolResult.failure("Secure storage key is not allow-listed.", "secure_key_denied")
            }
            val prefs = context.application.getSharedPreferences(context.options.secureStorage.preferencesName, Application.MODE_PRIVATE)
            prefs.edit().putString(key, args["value"] ?: "").apply()
            AndroidToolResult.success(JSONObject().put("key", key).put("written", true))
        })
        tools.add(secureTool("secure.remove_key", "Remove Secure Storage Key", "Removes an explicitly allow-listed secure value.", ToolScope.Delete) { args, context ->
            val key = args["key"] ?: return@secureTool AndroidToolResult.failure("Secure storage key is required.", "secure_key_required")
            if (!context.options.secureStorage.isAllowed(key)) {
                return@secureTool AndroidToolResult.failure("Secure storage key is not allow-listed.", "secure_key_denied")
            }
            val prefs = context.application.getSharedPreferences(context.options.secureStorage.preferencesName, Application.MODE_PRIVATE)
            prefs.edit().remove(key).apply()
            AndroidToolResult.success(JSONObject().put("key", key).put("removed", true))
        })

        tools.add(dataTool("data.list_databases", "List Databases", "Lists SQLite database files in app-owned roots.") { _, context ->
            val roots = FileSandbox.roots(context.application)
            val databases = roots.values.flatMap { root ->
                root.walkTopDown().maxDepth(4).filter { it.isFile && SQLiteSupport.isDatabase(it) }.toList()
            }.distinctBy { it.canonicalPath }
            AndroidToolResult.success(JSONObject()
                .put("databases", JSONArray(databases.map { FileSandbox.describePath(context.application, it) }))
                .put("count", databases.size))
        })
        tools.add(dataTool("data.describe_schema", "Describe Schema", "Describes a SQLite database schema.") { args, context ->
            val db = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            SQLiteSupport.openReadOnly(db.file).use { database ->
                AndroidToolResult.success(FileSandbox.describe(db).put("tables", SQLiteSupport.tables(database)))
            }
        })
        tools.add(dataTool("data.query", "Query Database", "Runs a read-only SQLite query.", ToolScope.Read) { args, context ->
            val sql = args["sql"]?.trim() ?: return@dataTool AndroidToolResult.failure("SQL query is required.", "sql_required")
            if (!SQLiteSupport.isReadOnly(sql)) {
                return@dataTool AndroidToolResult.failure("Only read-only SQLite queries are supported.", "sql_not_read_only")
            }
            val db = FileSandbox.resolve(context.application, args, requireExisting = true, expectDirectory = false)
            SQLiteSupport.openReadOnly(db.file).use { database ->
                AndroidToolResult.success(FileSandbox.describe(db).put("query", SQLiteSupport.query(database, sql, args.intArg("limit", 100))))
            }
        })
        tools.addAll(AndroidReflectionTools.create())
        return tools
    }

    private fun uiTool(
        id: String,
        name: String,
        description: String,
        scope: ToolScope = ToolScope.Read,
        handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
    ) = simpleTool(id, name, description, "ui", scope, "ui android view screenshot overlay", handler)

    private fun fileTool(
        id: String,
        name: String,
        description: String,
        scope: ToolScope = ToolScope.Read,
        handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
    ) = simpleTool(id, name, description, "files", scope, "files sandbox app data cache", handler)

    private fun prefsTool(
        id: String,
        name: String,
        description: String,
        scope: ToolScope = ToolScope.Read,
        handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
    ) = simpleTool(id, name, description, "prefs", scope, "preferences sharedpreferences settings", handler)

    private fun secureTool(
        id: String,
        name: String,
        description: String,
        scope: ToolScope = ToolScope.Read,
        handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
    ) = simpleTool(
        id,
        name,
        description,
        "secure",
        scope,
        "secure storage keystore allow-list",
        handler,
        ToolSecurity(ToolSecurityLevel.Critical, listOf("AccessesSecureStorage")),
    )

    private fun dataTool(
        id: String,
        name: String,
        description: String,
        scope: ToolScope = ToolScope.Read,
        handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
    ) = simpleTool(
        id,
        name,
        description,
        "data",
        scope,
        "sqlite database query schema",
        handler,
        ToolSecurity(ToolSecurityLevel.High, listOf("AccessesDatabase")),
    )

    private fun simpleTool(
        id: String,
        name: String,
        description: String,
        category: String,
        scope: ToolScope,
        keywords: String,
        handler: (Map<String, String>, AndroidToolExecutionContext) -> AndroidToolResult,
        security: ToolSecurity = ToolSecurity.Unspecified,
    ): AndroidTool = FunctionAndroidTool(
        ToolDefinition(
            id = id,
            name = name,
            description = description,
            category = category,
            scope = scope,
            keywords = keywords,
            argumentsSchema = ToolSchema.obj(additionalProperties = true),
            resultSchema = ToolSchema.obj(additionalProperties = true),
            security = security,
        ),
        handler,
    )

    private fun preferences(application: Application, args: Map<String, String>): Pair<String, SharedPreferences> {
        val name = args["name"]?.trim()?.ifBlank { null } ?: "${application.packageName}_preferences"
        return name to application.getSharedPreferences(name, Application.MODE_PRIVATE)
    }
}

private object FileSandbox {
    data class Resolved(val rootAlias: String, val root: File, val file: File) {
        fun relativePath(child: File = file): String = child.canonicalFile.relativeTo(root.canonicalFile).path
    }

    fun roots(application: Application): Map<String, File> {
        val roots = linkedMapOf<String, File>()
        roots["appData"] = application.filesDir
        roots["cache"] = application.cacheDir
        if (Build.VERSION.SDK_INT >= 21) {
            application.noBackupFilesDir?.let { roots["noBackup"] = it }
        }
        application.getExternalFilesDir(null)?.let { roots["externalFiles"] = it }
        application.getDatabasePath("__ansight_probe__").parentFile?.let { roots["databases"] = it }
        return roots.mapValues { it.value.canonicalFile }
    }

    fun resolve(
        application: Application,
        args: Map<String, String>,
        pathKey: String = "path",
        rootKey: String = "root",
        requireExisting: Boolean,
        expectDirectory: Boolean,
    ): Resolved {
        val rootAlias = args[rootKey]?.trim()?.ifBlank { null } ?: "appData"
        val root = roots(application)[rootAlias] ?: error("Unknown root '$rootAlias'.")
        val relative = args[pathKey]?.trim()?.ifBlank { null } ?: "."
        val target = File(root, relative).canonicalFile
        require(target.path == root.path || target.path.startsWith(root.path + File.separator)) {
            "Path escapes approved root '$rootAlias'."
        }
        if (requireExisting) {
            require(target.exists()) { "Path '$relative' does not exist in root '$rootAlias'." }
        }
        if (target.exists()) {
            require(target.isDirectory == expectDirectory) {
                if (expectDirectory) "Path '$relative' is not a directory." else "Path '$relative' is not a file."
            }
        }
        return Resolved(rootAlias, root, target)
    }

    fun describe(resolved: Resolved): JSONObject = JSONObject()
        .put("rootAlias", resolved.rootAlias)
        .put("rootPath", resolved.root.path)
        .put("filePath", resolved.file.path)
        .put("relativePath", resolved.relativePath())
        .put("exists", resolved.file.exists())
        .put("isDirectory", resolved.file.isDirectory)
        .put("sizeBytes", if (resolved.file.isFile) resolved.file.length() else JSONObject.NULL)
        .put("lastModifiedEpochMs", if (resolved.file.exists()) resolved.file.lastModified() else JSONObject.NULL)

    fun describePath(application: Application, file: File): JSONObject {
        val rootEntry = roots(application).entries
            .filter { file.canonicalPath.startsWith(it.value.canonicalPath) }
            .maxBy { it.value.canonicalPath.length }
        val fallbackRoot = file.parentFile ?: file
        val resolved = if (rootEntry != null) Resolved(rootEntry.key, rootEntry.value, file.canonicalFile) else Resolved("unknown", fallbackRoot, file)
        return describe(resolved)
    }
}

private object SQLiteSupport {
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
        val normalized = sql.trim().toLowerCase(Locale.US)
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

private fun Map<String, String>.intArg(name: String, defaultValue: Int): Int = this[name]?.toIntOrNull() ?: defaultValue
private fun Map<String, String>.booleanArg(name: String, defaultValue: Boolean): Boolean {
    return when (this[name]?.trim()) {
        "true" -> true
        "false" -> false
        else -> defaultValue
    }
}
