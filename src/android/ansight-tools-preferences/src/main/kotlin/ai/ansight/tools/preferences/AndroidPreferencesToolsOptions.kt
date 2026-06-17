package ai.ansight.tools.preferences

data class AndroidPreferencesToolsOptions(
    val defaultStore: String? = null,
    val allowedStores: Set<String> = emptySet(),
    val allowedKeys: Set<String> = emptySet(),
    val allowedKeyPrefixes: Set<String> = emptySet(),
) {
    companion object {
        @JvmField
        val Default = AndroidPreferencesToolsOptions()

        @JvmStatic
        fun createBuilder(): AndroidPreferencesToolsOptionsBuilder = AndroidPreferencesToolsOptionsBuilder()
    }

    fun validated(): AndroidPreferencesToolsOptions {
        return copy(
            defaultStore = defaultStore?.trim()?.ifBlank { null },
            allowedStores = allowedStores.mapNotNull { it.trim().ifBlank { null } }.toSet(),
            allowedKeys = allowedKeys.mapNotNull { it.trim().ifBlank { null } }.toSet(),
            allowedKeyPrefixes = allowedKeyPrefixes.mapNotNull { it.trim().ifBlank { null } }.toSet(),
        )
    }

    fun isStoreAllowed(store: String): Boolean {
        if (allowedStores.isEmpty()) {
            return true
        }

        return allowedStores.any { it.equals(store, ignoreCase = true) }
    }

    fun isKeyAllowed(key: String): Boolean {
        if (allowedKeys.isEmpty() && allowedKeyPrefixes.isEmpty()) {
            return true
        }

        return key in allowedKeys || allowedKeyPrefixes.any { key.startsWith(it) }
    }
}

class AndroidPreferencesToolsOptionsBuilder {
    private var defaultStore: String? = null
    private val allowedStores = linkedSetOf<String>()
    private val allowedKeys = linkedSetOf<String>()
    private val allowedKeyPrefixes = linkedSetOf<String>()

    fun withDefaultStore(store: String?): AndroidPreferencesToolsOptionsBuilder {
        defaultStore = store?.trim()?.ifBlank { null }
        return this
    }

    fun allowStore(store: String): AndroidPreferencesToolsOptionsBuilder {
        val normalized = store.trim()
        require(normalized.isNotBlank()) { "Preferences store must not be blank." }
        allowedStores.add(normalized)
        return this
    }

    fun allowStores(stores: Iterable<String>): AndroidPreferencesToolsOptionsBuilder {
        stores.forEach { allowStore(it) }
        return this
    }

    fun allowKey(key: String): AndroidPreferencesToolsOptionsBuilder {
        val normalized = key.trim()
        require(normalized.isNotBlank()) { "Preferences key must not be blank." }
        allowedKeys.add(normalized)
        return this
    }

    fun allowKeys(keys: Iterable<String>): AndroidPreferencesToolsOptionsBuilder {
        keys.forEach { allowKey(it) }
        return this
    }

    fun allowKeyPrefix(keyPrefix: String): AndroidPreferencesToolsOptionsBuilder {
        val normalized = keyPrefix.trim()
        require(normalized.isNotBlank()) { "Preferences key prefix must not be blank." }
        allowedKeyPrefixes.add(normalized)
        return this
    }

    fun allowKeyPrefixes(keyPrefixes: Iterable<String>): AndroidPreferencesToolsOptionsBuilder {
        keyPrefixes.forEach { allowKeyPrefix(it) }
        return this
    }

    fun build(): AndroidPreferencesToolsOptions {
        return AndroidPreferencesToolsOptions(
            defaultStore = defaultStore,
            allowedStores = allowedStores,
            allowedKeys = allowedKeys,
            allowedKeyPrefixes = allowedKeyPrefixes,
        ).validated()
    }
}
