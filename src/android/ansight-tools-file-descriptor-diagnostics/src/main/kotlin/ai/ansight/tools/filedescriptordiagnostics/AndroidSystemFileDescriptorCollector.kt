package ai.ansight.tools.filedescriptordiagnostics

import android.system.ErrnoException
import android.system.Os
import android.system.OsConstants
import java.io.File
import java.util.Locale

internal object AndroidSystemFileDescriptorCollector : FileDescriptorCollector {
    private val descriptorDirectory = File("/proc/self/fd")
    private val descriptorInfoDirectory = File("/proc/self/fdinfo")
    private val processLimitsFile = File("/proc/self/limits")

    override fun snapshot(options: AndroidFileDescriptorDiagnosticsOptions): FileDescriptorSnapshot {
        val descriptorNumbers = descriptorNumbers()
        val descriptors = descriptorNumbers.mapNotNull { descriptor ->
            inspect(descriptor, options.includeTargets)
        }
        val currentLimits = limits()
        val scannedLimit = descriptorNumbers.lastOrNull()?.plus(1) ?: 0
        return FileDescriptorSnapshot(
            descriptors = descriptors,
            limits = currentLimits,
            scanComplete = true,
            scannedDescriptorLimit = scannedLimit,
        )
    }

    override fun count(): FileDescriptorCountSnapshot {
        val descriptorNumbers = descriptorNumbers()
        val openCount = descriptorNumbers.count { descriptor ->
            descriptorStat(descriptor) != null
        }
        return FileDescriptorCountSnapshot(
            count = openCount,
            limits = limits(),
            scanComplete = true,
            scannedDescriptorLimit = descriptorNumbers.lastOrNull()?.plus(1) ?: 0,
        )
    }

    override fun inspect(descriptor: Int, includeTarget: Boolean): FileDescriptorInfo? {
        if (descriptor < 0) return null
        val descriptorPath = File(descriptorDirectory, descriptor.toString()).path
        val stat = descriptorStat(descriptor) ?: return null
        val target = if (includeTarget) runCatching { Os.readlink(descriptorPath) }.getOrNull() else null
        val info = readDescriptorInfo(descriptor)
        val statusFlags = info["flags"]?.toLongOrNull(8)?.toInt()

        return FileDescriptorInfo(
            descriptor = descriptor,
            kind = descriptorKind(stat.st_mode, target),
            target = target,
            accessMode = accessMode(statusFlags),
            closeOnExec = statusFlags?.let { it and OsConstants.O_CLOEXEC != 0 },
            descriptorFlags = null,
            statusFlags = statusFlags,
            positionBytes = info["pos"]?.toLongOrNull(),
            inode = stat.st_ino,
        )
    }

    override fun limits(): FileDescriptorLimits {
        val line = runCatching {
            processLimitsFile.useLines { lines ->
                lines.firstOrNull { it.trimStart().startsWith("Max open files") }
            }
        }.getOrNull()
        if (line == null) {
            return FileDescriptorLimits(null, null, false)
        }

        val values = line.trim().split(Regex("\\s+"))
        if (values.size < 5) {
            return FileDescriptorLimits(null, null, false)
        }
        val softRaw = values[3]
        val hardRaw = values[4]
        return FileDescriptorLimits(
            softLimit = softRaw.toLongOrNull(),
            hardLimit = hardRaw.toLongOrNull(),
            hardLimitUnlimited = hardRaw.lowercase(Locale.US) == "unlimited",
        )
    }

    private fun readDescriptorInfo(descriptor: Int): Map<String, String> {
        val file = File(descriptorInfoDirectory, descriptor.toString())
        return runCatching {
            file.useLines { lines ->
                val values = linkedMapOf<String, String>()
                lines.forEach { line ->
                    val separator = line.indexOf(':')
                    if (separator > 0) {
                        values[line.substring(0, separator).trim()] = line.substring(separator + 1).trim()
                    }
                }
                values
            }
        }.getOrDefault(emptyMap())
    }

    private fun descriptorNumbers(): List<Int> = descriptorDirectory.list()
        ?.mapNotNull { it.toIntOrNull() }
        ?.filter { it >= 0 }
        ?.sorted()
        ?: throw IllegalStateException("Unable to enumerate /proc/self/fd.")

    private fun descriptorStat(descriptor: Int) = try {
        Os.stat(File(descriptorDirectory, descriptor.toString()).path)
    } catch (error: ErrnoException) {
        if (error.errno == OsConstants.ENOENT || error.errno == OsConstants.EBADF) {
            null
        } else {
            throw error
        }
    }

    private fun descriptorKind(mode: Int, target: String?): FileDescriptorKind {
        if (target?.startsWith("socket:[") == true) return FileDescriptorKind.Socket
        if (target?.startsWith("pipe:[") == true) return FileDescriptorKind.Pipe
        if (target?.startsWith("anon_inode:") == true) return FileDescriptorKind.AnonymousInode
        if (target?.startsWith("/memfd:") == true || target?.startsWith("memfd:") == true) return FileDescriptorKind.MemoryFile
        return when {
            OsConstants.S_ISREG(mode) -> FileDescriptorKind.RegularFile
            OsConstants.S_ISDIR(mode) -> FileDescriptorKind.Directory
            OsConstants.S_ISSOCK(mode) -> FileDescriptorKind.Socket
            OsConstants.S_ISFIFO(mode) -> FileDescriptorKind.Pipe
            OsConstants.S_ISCHR(mode) -> FileDescriptorKind.CharacterDevice
            OsConstants.S_ISBLK(mode) -> FileDescriptorKind.BlockDevice
            OsConstants.S_ISLNK(mode) -> FileDescriptorKind.SymbolicLink
            else -> FileDescriptorKind.Other
        }
    }

    private fun accessMode(statusFlags: Int?): String? {
        if (statusFlags == null) return null
        return when (statusFlags and OsConstants.O_ACCMODE) {
            OsConstants.O_RDONLY -> "read_only"
            OsConstants.O_WRONLY -> "write_only"
            OsConstants.O_RDWR -> "read_write"
            else -> "unknown"
        }
    }
}
