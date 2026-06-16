package ai.ansight.tools.visualtree

object AndroidVisualTreeProviderRegistry {
    const val NativeSource = "native"

    private val lock = Any()
    private val providers = linkedMapOf<String, AndroidVisualTreeProvider>(
        NativeSource to AndroidNativeVisualTreeProvider,
    )

    @JvmStatic
    @JvmOverloads
    fun register(provider: AndroidVisualTreeProvider, replaceExisting: Boolean = true) {
        val source = normalizeSource(provider.source)
        synchronized(lock) {
            require(replaceExisting || !providers.containsKey(source)) {
                "A visual tree provider for source '$source' is already registered."
            }
            providers[source] = provider
        }
    }

    @JvmStatic
    fun provider(source: String?): AndroidVisualTreeProvider? {
        val normalized = normalizeSourceOrDefault(source)
        return synchronized(lock) {
            providers[normalized]
        }
    }

    @JvmStatic
    fun registeredSources(): List<String> {
        return synchronized(lock) {
            providers.keys.sorted()
        }
    }

    fun normalizeSourceOrDefault(source: String?): String {
        val normalized = source?.trim()?.lowercase().orEmpty()
        return normalized.ifBlank { NativeSource }
    }

    private fun normalizeSource(source: String): String {
        val normalized = source.trim().lowercase()
        require(normalized.isNotBlank()) { "Visual tree provider source must not be blank." }
        require(normalized.length <= 64) { "Visual tree provider source must be at most 64 characters." }
        return normalized
    }
}
