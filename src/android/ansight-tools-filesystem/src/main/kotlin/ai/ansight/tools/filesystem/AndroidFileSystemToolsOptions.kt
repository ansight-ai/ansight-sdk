package ai.ansight.tools.filesystem

import java.io.File

data class AndroidFileSystemRoot(
    val alias: String,
    val path: String,
)

data class AndroidFileSystemToolsOptions(
    val additionalRoots: List<AndroidFileSystemRoot> = emptyList(),
) {
    companion object {
        @JvmField
        val Default = AndroidFileSystemToolsOptions()

        @JvmStatic
        fun createBuilder(): AndroidFileSystemToolsOptionsBuilder = AndroidFileSystemToolsOptionsBuilder()
    }

    fun validated(): AndroidFileSystemToolsOptions {
        return copy(
            additionalRoots = additionalRoots
                .mapNotNull { root ->
                    val alias = root.alias.trim()
                    val path = root.path.trim()
                    if (alias.isBlank() || path.isBlank()) {
                        null
                    } else {
                        AndroidFileSystemRoot(alias, path)
                    }
                }
                .distinctBy { it.alias.lowercase() },
        )
    }
}

class AndroidFileSystemToolsOptionsBuilder {
    private val rootsByAlias = linkedMapOf<String, AndroidFileSystemRoot>()

    fun addRoot(alias: String, path: String): AndroidFileSystemToolsOptionsBuilder {
        val normalizedAlias = alias.trim()
        val normalizedPath = path.trim()
        require(normalizedAlias.isNotBlank()) { "File system root alias must not be blank." }
        require(normalizedPath.isNotBlank()) { "File system root path must not be blank." }
        rootsByAlias[normalizedAlias.lowercase()] = AndroidFileSystemRoot(normalizedAlias, normalizedPath)
        return this
    }

    fun addRoot(alias: String, path: File): AndroidFileSystemToolsOptionsBuilder {
        return addRoot(alias, path.path)
    }

    fun build(): AndroidFileSystemToolsOptions {
        return AndroidFileSystemToolsOptions(rootsByAlias.values.toList()).validated()
    }
}
