package ai.ansight.runtime

import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.security.DigestInputStream
import java.security.MessageDigest
import java.util.UUID
import java.util.zip.CRC32
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/** Streams a sealed, leased native session to a deterministic ZIP archive. */
object OfflineCaptureArchive {
    data class Export(val path: File, val sha256: String, val byteCount: Long)

    fun write(lease: OfflineCaptureLease, destination: File): Export {
        require(!destination.canonicalPath.startsWith(lease.directory.canonicalPath + File.separator))
        val files = lease.directory.walkTopDown()
            .onEnter { it.canonicalFile == it.absoluteFile }
            .filter { it.isFile && it.extension != "tmp" && it.canonicalFile == it.absoluteFile }
            .sortedBy { it.path }.toList()
        require(files.size <= 65535) { "Too many offline archive entries." }
        val parent = destination.absoluteFile.parentFile!!
        if (!parent.isDirectory && !parent.mkdirs()) throw IOException("Could not create export directory.")
        val temporary = File(parent, "${UUID.randomUUID()}.part")
        try {
            FileOutputStream(temporary).use { output ->
                ZipOutputStream(output).use { zip ->
                    for (file in files) {
                        val crc = CRC32()
                        val buffer = ByteArray(64 * 1024)
                        file.inputStream().use { input ->
                            while (true) {
                                val count = input.read(buffer)
                                if (count < 0) break
                                crc.update(buffer, 0, count)
                            }
                        }
                        val entry = ZipEntry(file.relativeTo(lease.directory).invariantSeparatorsPath).apply {
                            time = 0
                            method = ZipEntry.STORED
                            size = file.length()
                            compressedSize = size
                            this.crc = crc.value
                        }
                        zip.putNextEntry(entry)
                        file.inputStream().use { it.copyTo(zip, 64 * 1024) }
                        zip.closeEntry()
                    }
                    zip.finish()
                    zip.flush()
                    output.fd.sync()
                }
            }
            check(!destination.exists()) { "Export destination already exists." }
            if (!temporary.renameTo(destination)) throw IOException("Could not commit offline archive.")
            val digest = MessageDigest.getInstance("SHA-256")
            DigestInputStream(destination.inputStream(), digest).use { input ->
                val buffer = ByteArray(64 * 1024)
                while (input.read(buffer) >= 0) { /* Bound hashing memory to one chunk. */ }
            }
            return Export(destination, digest.digest().joinToString("") { "%02x".format(it) }, destination.length())
        } finally { temporary.delete() }
    }
}
