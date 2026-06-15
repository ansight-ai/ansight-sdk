package ai.ansight.runtime

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class DeviceProfilesTest {
    @Test
    fun runtimeProfileSerializesProtocolShape() {
        val json = DeviceRuntimeProfile(
            primary = 1,
            primaryVersion = "16",
            engine = DeviceRuntimeEngineProfile(name = "ART", version = "2.1"),
            stack = listOf(
                DeviceRuntimeStackEntry(runtimeCode = 250, name = "Kotlin", version = "2.2.0", layer = "language"),
                DeviceRuntimeStackEntry(runtimeCode = 1, name = "Android", version = "16", layer = "platform"),
            ),
            aotEnabled = true,
            jitEnabled = true,
        ).toJson()

        assertEquals(1, json.getInt("primary"))
        assertEquals("16", json.getString("primaryVersion"))
        assertEquals("ART", json.getJSONObject("engine").getString("name"))
        assertEquals("2.1", json.getJSONObject("engine").getString("version"))
        assertEquals(250, json.getJSONArray("stack").getJSONObject(0).getInt("runtimeCode"))
        assertEquals("language", json.getJSONArray("stack").getJSONObject(0).getString("layer"))
        assertTrue(json.getBoolean("aotEnabled"))
        assertTrue(json.getBoolean("jitEnabled"))
        assertFalse(json.has("platformName"))
        assertFalse(json.has("runtimeName"))
    }
}
