package ai.ansight.runtime

import android.app.Application
import android.content.SharedPreferences
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class HostConnectionStatusListenerTest {
    @Test
    fun listenerReceivesCurrentStatusAndForcedConfigRefresh() {
        val statuses = mutableListOf<HostConnectionStatus>()
        val capabilities = mutableListOf<HostConnectionCapabilities>()
        val subscription = AnsightRuntime.addHostConnectionStatusListener(
            HostConnectionStatusListener { status, nextCapabilities ->
                statuses.add(status)
                capabilities.add(nextCapabilities)
            },
            emitCurrent = false,
        )

        try {
            AnsightRuntime.initialize(
                TestApplication(),
                AnsightOptions(
                    hostConnection = AnsightHostConnectionOptions(
                        bundledConfigJson = """{"schema":"test"}""",
                    ),
                ),
            )

            assertEquals(1, statuses.size)
            assertFalse(statuses.last().isRuntimeActive)
            assertTrue(statuses.last().hasBundledConfig)
            assertTrue(capabilities.last().canConnectUsingBundledConfig)

            val result = AnsightRuntime.notifyHostConnectionConfigChanged()

            assertTrue(result.success)
            assertEquals(HostConnectionActionKind.NotifyConfigChanged, result.kind)
            assertEquals(2, statuses.size)
            assertTrue(statuses.last().hasBundledConfig)

            subscription.remove()
            AnsightRuntime.notifyHostConnectionConfigChanged()

            assertEquals(2, statuses.size)
        } finally {
            subscription.remove()
            AnsightRuntime.deactivate()
        }
    }

    private class TestApplication : Application() {
        private val preferences = TestSharedPreferences()

        override fun getPackageName(): String = "ai.ansight.test"

        override fun getSharedPreferences(name: String?, mode: Int): SharedPreferences = preferences
    }

    private class TestSharedPreferences : SharedPreferences {
        private val values = linkedMapOf<String, Any?>()

        override fun getAll(): MutableMap<String, *> = LinkedHashMap(values)

        override fun getString(key: String?, defValue: String?): String? = values[key] as? String ?: defValue

        override fun getStringSet(key: String?, defValues: MutableSet<String>?): MutableSet<String>? = defValues

        override fun getInt(key: String?, defValue: Int): Int = values[key] as? Int ?: defValue

        override fun getLong(key: String?, defValue: Long): Long = values[key] as? Long ?: defValue

        override fun getFloat(key: String?, defValue: Float): Float = values[key] as? Float ?: defValue

        override fun getBoolean(key: String?, defValue: Boolean): Boolean = values[key] as? Boolean ?: defValue

        override fun contains(key: String?): Boolean = values.containsKey(key)

        override fun edit(): SharedPreferences.Editor = TestEditor(values)

        override fun registerOnSharedPreferenceChangeListener(listener: SharedPreferences.OnSharedPreferenceChangeListener?) = Unit

        override fun unregisterOnSharedPreferenceChangeListener(listener: SharedPreferences.OnSharedPreferenceChangeListener?) = Unit
    }

    private class TestEditor(
        private val values: MutableMap<String, Any?>,
    ) : SharedPreferences.Editor {
        private val pending = linkedMapOf<String, Any?>()
        private var shouldClear = false

        override fun putString(key: String?, value: String?): SharedPreferences.Editor = apply {
            if (key != null) {
                pending[key] = value
            }
        }

        override fun putStringSet(key: String?, values: MutableSet<String>?): SharedPreferences.Editor = this

        override fun putInt(key: String?, value: Int): SharedPreferences.Editor = apply {
            if (key != null) {
                pending[key] = value
            }
        }

        override fun putLong(key: String?, value: Long): SharedPreferences.Editor = apply {
            if (key != null) {
                pending[key] = value
            }
        }

        override fun putFloat(key: String?, value: Float): SharedPreferences.Editor = apply {
            if (key != null) {
                pending[key] = value
            }
        }

        override fun putBoolean(key: String?, value: Boolean): SharedPreferences.Editor = apply {
            if (key != null) {
                pending[key] = value
            }
        }

        override fun remove(key: String?): SharedPreferences.Editor = apply {
            if (key != null) {
                pending[key] = null
            }
        }

        override fun clear(): SharedPreferences.Editor = apply {
            shouldClear = true
        }

        override fun commit(): Boolean {
            apply()
            return true
        }

        override fun apply() {
            if (shouldClear) {
                values.clear()
            }
            pending.forEach { (key, value) ->
                if (value == null) {
                    values.remove(key)
                } else {
                    values[key] = value
                }
            }
        }
    }
}
