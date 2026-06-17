package ai.ansight.tools.database

import java.io.File

data class AndroidDatabaseRoot(
    val alias: String,
    val path: String,
)

data class AndroidDatabaseToolsOptions(
    val additionalRoots: List<AndroidDatabaseRoot> = emptyList(),
    val includePlatformRoots: Boolean = true,
) {
    companion object {
        @JvmField
        val Default = AndroidDatabaseToolsOptions()

        @JvmStatic
        fun createBuilder(): AndroidDatabaseToolsOptionsBuilder = AndroidDatabaseToolsOptionsBuilder()
    }

    fun validated(): AndroidDatabaseToolsOptions {
        return copy(
            additionalRoots = additionalRoots
                .mapNotNull { root ->
                    val alias = root.alias.trim()
                    val path = root.path.trim()
                    if (alias.isBlank() || path.isBlank()) {
                        null
                    } else {
                        AndroidDatabaseRoot(alias, path)
                    }
                }
                .distinctBy { it.alias.lowercase() },
        )
    }
}

class AndroidDatabaseToolsOptionsBuilder {
    private val rootsByAlias = linkedMapOf<String, AndroidDatabaseRoot>()
    private var includePlatformRoots = true

    fun addRoot(alias: String, path: String): AndroidDatabaseToolsOptionsBuilder {
        val normalizedAlias = alias.trim()
        val normalizedPath = path.trim()
        require(normalizedAlias.isNotBlank()) { "Database root alias must not be blank." }
        require(normalizedPath.isNotBlank()) { "Database root path must not be blank." }
        rootsByAlias[normalizedAlias.lowercase()] = AndroidDatabaseRoot(normalizedAlias, normalizedPath)
        return this
    }

    fun addRoot(alias: String, path: File): AndroidDatabaseToolsOptionsBuilder {
        return addRoot(alias, path.path)
    }

    fun includePlatformRoots(includePlatformRoots: Boolean): AndroidDatabaseToolsOptionsBuilder {
        this.includePlatformRoots = includePlatformRoots
        return this
    }

    fun build(): AndroidDatabaseToolsOptions {
        return AndroidDatabaseToolsOptions(
            additionalRoots = rootsByAlias.values.toList(),
            includePlatformRoots = includePlatformRoots,
        ).validated()
    }
}
