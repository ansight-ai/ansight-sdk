package ai.ansight.tools.reflection

object AndroidReflectionRootRegistry {
    private val lock = Any()
    private val roots = linkedMapOf<String, RegisteredAndroidReflectionRoot>()

    @JvmStatic
    @JvmOverloads
    fun register(
        id: String,
        value: Any,
        displayName: String = id,
        description: String? = null,
    ): AndroidReflectionRootRegistration {
        val normalizedId = normalizeId(id)
        val metadata = ReflectionRootMetadata(
            id = normalizedId,
            displayName = displayName.trim().ifBlank { normalizedId },
            description = description?.trim()?.ifBlank { null },
            referenceType = "strong",
            resolve = { value },
        )
        synchronized(lock) {
            roots[normalizedId] = metadata
        }
        return AndroidReflectionRootRegistration(normalizedId)
    }

    @JvmStatic
    @JvmOverloads
    fun registerGetter(
        id: String,
        displayName: String = id,
        description: String? = null,
        getter: () -> Any?,
    ): AndroidReflectionRootRegistration {
        val normalizedId = normalizeId(id)
        val metadata = ReflectionRootMetadata(
            id = normalizedId,
            displayName = displayName.trim().ifBlank { normalizedId },
            description = description?.trim()?.ifBlank { null },
            referenceType = "getter",
            resolve = getter,
        )
        synchronized(lock) {
            roots[normalizedId] = metadata
        }
        return AndroidReflectionRootRegistration(normalizedId)
    }

    @JvmStatic
    fun deregister(id: String): Boolean {
        val normalizedId = id.trim()
        if (normalizedId.isBlank()) {
            return false
        }
        return synchronized(lock) {
            roots.remove(normalizedId) != null
        }
    }

    @JvmStatic
    fun clear() {
        synchronized(lock) {
            roots.clear()
        }
    }

    internal fun snapshot(): List<RegisteredAndroidReflectionRoot> = synchronized(lock) {
        roots.values.toList()
    }

    private fun normalizeId(id: String): String {
        val normalized = id.trim()
        require(normalized.isNotBlank()) { "Reflection root id must not be blank." }
        return normalized
    }

    private data class ReflectionRootMetadata(
        override val id: String,
        override val displayName: String,
        override val description: String?,
        override val referenceType: String,
        override val resolve: () -> Any?,
    ) : RegisteredAndroidReflectionRoot
}

class AndroidReflectionRootRegistration internal constructor(
    private val id: String,
) : AutoCloseable {
    override fun close() {
        AndroidReflectionRootRegistry.deregister(id)
    }
}

internal interface RegisteredAndroidReflectionRoot {
    val id: String
    val displayName: String
    val description: String?
    val referenceType: String
    val resolve: () -> Any?
}
