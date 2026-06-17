package ai.ansight.tools.reflection

data class AndroidReflectionToolsOptions(
    val includeBuiltInRoots: Boolean = true,
    val allowedRootIds: Set<String> = emptySet(),
    val allowedTypePrefixes: Set<String> = emptySet(),
) {
    companion object {
        @JvmField
        val Default = AndroidReflectionToolsOptions()

        @JvmStatic
        fun createBuilder(): AndroidReflectionToolsOptionsBuilder = AndroidReflectionToolsOptionsBuilder()
    }

    fun validated(): AndroidReflectionToolsOptions {
        return copy(
            allowedRootIds = allowedRootIds.mapNotNull { it.trim().ifBlank { null } }.toSet(),
            allowedTypePrefixes = allowedTypePrefixes.mapNotNull { it.trim().ifBlank { null } }.toSet(),
        )
    }

    fun isRootAllowed(rootId: String): Boolean {
        return allowedRootIds.isEmpty() || rootId in allowedRootIds
    }

    fun isTypeAllowed(typeName: String): Boolean {
        return allowedTypePrefixes.isEmpty() || allowedTypePrefixes.any { typeName.startsWith(it) }
    }
}

class AndroidReflectionToolsOptionsBuilder {
    private var includeBuiltInRoots = true
    private val allowedRootIds = linkedSetOf<String>()
    private val allowedTypePrefixes = linkedSetOf<String>()

    fun includeBuiltInRoots(includeBuiltInRoots: Boolean): AndroidReflectionToolsOptionsBuilder {
        this.includeBuiltInRoots = includeBuiltInRoots
        return this
    }

    fun allowRoot(rootId: String): AndroidReflectionToolsOptionsBuilder {
        val normalized = rootId.trim()
        require(normalized.isNotBlank()) { "Reflection root id must not be blank." }
        allowedRootIds.add(normalized)
        return this
    }

    fun allowRoots(rootIds: Iterable<String>): AndroidReflectionToolsOptionsBuilder {
        rootIds.forEach { allowRoot(it) }
        return this
    }

    fun allowTypePrefix(typePrefix: String): AndroidReflectionToolsOptionsBuilder {
        val normalized = typePrefix.trim()
        require(normalized.isNotBlank()) { "Reflection type prefix must not be blank." }
        allowedTypePrefixes.add(normalized)
        return this
    }

    fun allowTypePrefixes(typePrefixes: Iterable<String>): AndroidReflectionToolsOptionsBuilder {
        typePrefixes.forEach { allowTypePrefix(it) }
        return this
    }

    fun build(): AndroidReflectionToolsOptions {
        return AndroidReflectionToolsOptions(
            includeBuiltInRoots = includeBuiltInRoots,
            allowedRootIds = allowedRootIds,
            allowedTypePrefixes = allowedTypePrefixes,
        ).validated()
    }
}
