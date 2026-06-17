package ai.ansight.tools.filesystem

import android.app.Application
import android.os.Build
import org.json.JSONObject
import java.io.File

object AndroidFileSandbox {
    data class Resolved(val rootAlias: String, val root: File, val file: File) {
        fun relativePath(child: File = file): String = child.canonicalFile.relativeTo(root.canonicalFile).path
    }

    fun roots(
        application: Application,
        options: AndroidFileSystemToolsOptions = AndroidFileSystemToolsOptions.Default,
    ): Map<String, File> {
        val roots = linkedMapOf<String, File>()
        roots["appData"] = application.filesDir
        roots["cache"] = application.cacheDir
        if (Build.VERSION.SDK_INT >= 21) {
            application.noBackupFilesDir?.let { roots["noBackup"] = it }
        }
        application.getExternalFilesDir(null)?.let { roots["externalFiles"] = it }
        application.getDatabasePath("__ansight_probe__").parentFile?.let { roots["databases"] = it }
        options.validated().additionalRoots.forEach { root ->
            roots[root.alias] = File(root.path)
        }
        return roots.mapValues { it.value.canonicalFile }
    }

    fun resolve(
        application: Application,
        args: Map<String, String>,
        options: AndroidFileSystemToolsOptions = AndroidFileSystemToolsOptions.Default,
        pathKey: String = "path",
        rootKey: String = "root",
        requireExisting: Boolean,
        expectDirectory: Boolean,
    ): Resolved {
        val rootAlias = args[rootKey]?.trim()?.ifBlank { null } ?: "appData"
        val rootEntry = roots(application, options).entries
            .firstOrNull { it.key.equals(rootAlias, ignoreCase = true) }
            ?: error("Unknown root '$rootAlias'.")
        val root = rootEntry.value
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
        return Resolved(rootEntry.key, root, target)
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

    fun describePath(
        application: Application,
        file: File,
        options: AndroidFileSystemToolsOptions = AndroidFileSystemToolsOptions.Default,
    ): JSONObject {
        val rootEntry = roots(application, options).entries
            .filter { file.canonicalPath.startsWith(it.value.canonicalPath) }
            .maxByOrNull { it.value.canonicalPath.length }
        val fallbackRoot = file.parentFile ?: file
        val resolved = if (rootEntry != null) {
            Resolved(rootEntry.key, rootEntry.value, file.canonicalFile)
        } else {
            Resolved("unknown", fallbackRoot, file)
        }
        return describe(resolved)
    }
}
