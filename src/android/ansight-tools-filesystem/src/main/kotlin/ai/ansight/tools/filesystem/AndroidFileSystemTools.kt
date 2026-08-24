package ai.ansight.tools.filesystem

import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AndroidToolResult
import ai.ansight.runtime.BinaryTransferDescriptor
import ai.ansight.runtime.PairingFileTransferWireProtocol
import ai.ansight.runtime.ToolPolicy
import ai.ansight.runtime.androidFileTool
import ai.ansight.runtime.booleanArg
import ai.ansight.runtime.intArg
import ai.ansight.runtime.sendBinaryTransfer
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.nio.charset.Charset
import java.security.MessageDigest
import java.util.Locale

object AndroidFileSystemTools {
    @JvmStatic
    @JvmOverloads
    fun create(options: AndroidFileSystemToolsOptions = AndroidFileSystemToolsOptions.Default): List<AndroidTool> = listOf(
        androidFileTool(
            FileSystemToolIds.ListDirectory,
            "List Directory",
            "Lists files inside an approved app root.",
        ) { args, context ->
            val directory = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = true)
            val entries = directory.file.listFiles()?.sortedBy { it.name.lowercase(Locale.US) } ?: emptyList()
            AndroidToolResult.success(
                AndroidFileSandbox.describe(directory).put(
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
        },
        androidFileTool(
            FileSystemToolIds.ReadFile,
            "Read File",
            "Reads a UTF-8 text file inside an approved app root.",
        ) { args, context ->
            val file = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = false)
            val maxBytes = args.intArg("maxBytes", 256 * 1024).coerceIn(1, 1024 * 1024)
            val bytes = file.file.readBytes()
            if (bytes.size > maxBytes) {
                return@androidFileTool AndroidToolResult.failure("File exceeds maxBytes.", "file_too_large", AndroidFileSandbox.describe(file))
            }
            AndroidToolResult.success(
                AndroidFileSandbox.describe(file)
                    .put("text", bytes.toString(Charset.forName(args["encoding"] ?: "UTF-8")))
                    .put("encoding", args["encoding"] ?: "UTF-8"),
            )
        },
        androidFileTool(
            FileSystemToolIds.GetFileChecksum,
            "Get File Checksum",
            "Returns a SHA-256 checksum for a file.",
        ) { args, context ->
            val file = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = false)
            val digest = MessageDigest.getInstance("SHA-256").digest(file.file.readBytes()).joinToString("") { "%02x".format(it) }
            AndroidToolResult.success(AndroidFileSandbox.describe(file).put("sha256", digest))
        },
        androidFileTool(
            FileSystemToolIds.DownloadFile,
            "Download File",
            "Returns a small file inline as base64.",
        ) { args, context ->
            val file = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = false)
            val maxBytes = args.intArg("maxBytes", 512 * 1024).coerceIn(1, 1024 * 1024)
            val bytes = file.file.readBytes()
            if (bytes.size > maxBytes) {
                return@androidFileTool AndroidToolResult.failure(
                    "File exceeds maxBytes; use files.begin_binary_download.",
                    "file_too_large",
                    AndroidFileSandbox.describe(file),
                )
            }
            AndroidToolResult.success(
                AndroidFileSandbox.describe(file)
                    .put("contentBase64", Base64.encodeToString(bytes, Base64.NO_WRAP))
                    .put("deliveryMode", "inline_base64"),
            )
        },
        androidFileTool(
            FileSystemToolIds.BeginBinaryDownload,
            "Begin Binary Download",
            "Transfers a file over the WebSocket binary channel.",
        ) { args, context ->
            val file = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = false)
            val transferId = args["transferId"]?.trim()?.ifBlank { null } ?: PairingFileTransferWireProtocol.newTransferId()
            val downloadId = args["downloadId"]?.trim()?.ifBlank { null } ?: context.requestId ?: transferId
            val chunkBytes = args.intArg("chunkBytes", 64 * 1024)
            val bytes = file.file.readBytes()
            val descriptor = AndroidFileSandbox.describe(file)
                .put(
                    "transfer",
                    BinaryTransferDescriptor(
                        transferId = transferId,
                        downloadId = downloadId,
                        fileName = file.file.name,
                        mimeType = args["mimeType"] ?: "application/octet-stream",
                        sizeBytes = bytes.size.toLong(),
                        chunkBytes = chunkBytes,
                        status = if (context.transport?.isOpen == true) "started" else "unavailable",
                    ).toJson(),
                )
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
        },
        androidFileTool(
            FileSystemToolIds.PushFile,
            "Push File",
            "Writes a file inside an approved app root.",
            ToolPolicy.Write,
        ) { args, context ->
            val file = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = false, expectDirectory = false)
            file.file.parentFile?.mkdirs()
            val bytes = args["contentBase64"]?.let { Base64.decode(it, Base64.DEFAULT) }
                ?: (args["content"] ?: "").toByteArray(Charsets.UTF_8)
            file.file.writeBytes(bytes)
            AndroidToolResult.success(AndroidFileSandbox.describe(file).put("writtenBytes", bytes.size))
        },
        androidFileTool(
            FileSystemToolIds.CopyFile,
            "Copy File",
            "Copies a file between approved app roots.",
            ToolPolicy.Write,
        ) { args, context ->
            val source = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = false)
            val destination = AndroidFileSandbox.resolve(
                context.application,
                args,
                options,
                pathKey = "destinationPath",
                rootKey = "destinationRoot",
                requireExisting = false,
                expectDirectory = false,
            )
            destination.file.parentFile?.mkdirs()
            source.file.copyTo(destination.file, overwrite = args.booleanArg("overwrite", false))
            AndroidToolResult.success(AndroidFileSandbox.describe(destination))
        },
        androidFileTool(
            FileSystemToolIds.MoveFile,
            "Move File",
            "Moves a file between approved app roots.",
            ToolPolicy.Write,
        ) { args, context ->
            val source = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = false)
            val destination = AndroidFileSandbox.resolve(
                context.application,
                args,
                options,
                pathKey = "destinationPath",
                rootKey = "destinationRoot",
                requireExisting = false,
                expectDirectory = false,
            )
            destination.file.parentFile?.mkdirs()
            if (!source.file.renameTo(destination.file)) {
                source.file.copyTo(destination.file, overwrite = args.booleanArg("overwrite", false))
                source.file.delete()
            }
            AndroidToolResult.success(AndroidFileSandbox.describe(destination))
        },
        androidFileTool(
            FileSystemToolIds.DeleteFile,
            "Delete File",
            "Deletes a file inside an approved app root.",
            ToolPolicy.Critical,
        ) { args, context ->
            val file = AndroidFileSandbox.resolve(context.application, args, options, requireExisting = true, expectDirectory = false)
            val deleted = file.file.delete()
            AndroidToolResult.success(AndroidFileSandbox.describe(file).put("deleted", deleted))
        },
    )
}
