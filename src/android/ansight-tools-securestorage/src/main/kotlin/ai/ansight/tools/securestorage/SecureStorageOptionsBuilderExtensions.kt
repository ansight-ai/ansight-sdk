package ai.ansight.tools.securestorage

import ai.ansight.runtime.AnsightOptionsBuilder
import ai.ansight.runtime.AnsightSecureStorageOptions

class AndroidSecureStorageToolsOptionsBuilder {
    private var preferencesName: String = "ai.ansight.secure-storage"
    private val allowedKeys = linkedSetOf<String>()
    private val allowedPrefixes = linkedSetOf<String>()

    fun withPreferencesName(preferencesName: String): AndroidSecureStorageToolsOptionsBuilder {
        val normalized = preferencesName.trim()
        require(normalized.isNotBlank()) { "Secure storage preferences name must not be blank." }
        this.preferencesName = normalized
        return this
    }

    fun allowKey(key: String): AndroidSecureStorageToolsOptionsBuilder {
        val normalized = key.trim()
        require(normalized.isNotBlank()) { "Secure storage key must not be blank." }
        allowedKeys.add(normalized)
        return this
    }

    fun allowKeys(keys: Iterable<String>): AndroidSecureStorageToolsOptionsBuilder {
        keys.forEach { allowKey(it) }
        return this
    }

    fun allowKeyPrefix(keyPrefix: String): AndroidSecureStorageToolsOptionsBuilder {
        val normalized = keyPrefix.trim()
        require(normalized.isNotBlank()) { "Secure storage key prefix must not be blank." }
        allowedPrefixes.add(normalized)
        return this
    }

    fun allowKeyPrefixes(keyPrefixes: Iterable<String>): AndroidSecureStorageToolsOptionsBuilder {
        keyPrefixes.forEach { allowKeyPrefix(it) }
        return this
    }

    fun build(): AnsightSecureStorageOptions {
        return AnsightSecureStorageOptions(
            preferencesName = preferencesName,
            allowedKeys = allowedKeys,
            allowedPrefixes = allowedPrefixes,
        ).validated()
    }
}

fun AnsightOptionsBuilder.withSecureStorageTools(): AnsightOptionsBuilder {
    return addTools(AndroidSecureStorageTools.create())
}

fun AnsightOptionsBuilder.withSecureStorageTools(
    options: AnsightSecureStorageOptions,
): AnsightOptionsBuilder {
    return withSecureStorage(options).addTools(AndroidSecureStorageTools.create())
}

fun AnsightOptionsBuilder.withSecureStorageTools(
    configure: AndroidSecureStorageToolsOptionsBuilder.() -> Unit,
): AnsightOptionsBuilder {
    val builder = AndroidSecureStorageToolsOptionsBuilder()
    builder.configure()
    return withSecureStorageTools(builder.build())
}
