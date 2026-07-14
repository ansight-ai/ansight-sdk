package ai.ansight.tools.filedescriptordiagnostics

import org.json.JSONObject

internal enum class FileDescriptorKind(val wireName: String) {
    RegularFile("regular_file"),
    Directory("directory"),
    Socket("socket"),
    Pipe("pipe"),
    CharacterDevice("character_device"),
    BlockDevice("block_device"),
    SymbolicLink("symbolic_link"),
    AnonymousInode("anonymous_inode"),
    MemoryFile("memory_file"),
    Other("other"),
    Unknown("unknown");

    companion object {
        fun fromWireName(value: String): FileDescriptorKind? =
            values().firstOrNull { it.wireName == value }
    }
}

internal data class FileDescriptorInfo(
    val descriptor: Int,
    val kind: FileDescriptorKind,
    val target: String?,
    val accessMode: String?,
    val closeOnExec: Boolean?,
    val descriptorFlags: Int?,
    val statusFlags: Int?,
    val positionBytes: Long?,
    val inode: Long?,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("descriptor", descriptor)
        .put("kind", kind.wireName)
        .put("target", target ?: JSONObject.NULL)
        .put("accessMode", accessMode ?: JSONObject.NULL)
        .put("closeOnExec", closeOnExec ?: JSONObject.NULL)
        .put("descriptorFlags", descriptorFlags ?: JSONObject.NULL)
        .put("statusFlags", statusFlags ?: JSONObject.NULL)
        .put("positionBytes", positionBytes ?: JSONObject.NULL)
        .put("inode", inode ?: JSONObject.NULL)
}

internal data class FileDescriptorLimits(
    val softLimit: Long?,
    val hardLimit: Long?,
    val hardLimitUnlimited: Boolean,
)

internal data class FileDescriptorSnapshot(
    val descriptors: List<FileDescriptorInfo>,
    val limits: FileDescriptorLimits,
    val scanComplete: Boolean,
    val scannedDescriptorLimit: Int,
)

internal data class FileDescriptorCountSnapshot(
    val count: Int,
    val limits: FileDescriptorLimits,
    val scanComplete: Boolean,
    val scannedDescriptorLimit: Int,
)

internal interface FileDescriptorCollector {
    fun snapshot(options: AndroidFileDescriptorDiagnosticsOptions): FileDescriptorSnapshot
    fun count(): FileDescriptorCountSnapshot
    fun inspect(descriptor: Int, includeTarget: Boolean): FileDescriptorInfo?
    fun limits(): FileDescriptorLimits
}
