package ai.ansight.runtime

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class MetricGroupTest {
    @Test
    fun typedGroupWinsOverLegacyLabelAndSurvivesWireMetadata() {
        val group = AnsightMetricGroup(" checkout ", " Checkout ")
        val channel = AnsightChannel(64, "Payment latency", unit = "ms", group = "Old label", metricGroup = group).validated()
        val json = channel.toJson()

        assertEquals("Checkout", channel.group)
        assertEquals("checkout", json.getJSONObject("metricGroup").getString("id"))
        assertEquals("Checkout", json.getString("group"))
        assertEquals(channel.metricGroup, AnsightMetricGroup.fromJson(json.getJSONObject("metricGroup")))
    }

    @Test
    fun legacyChannelsAndInvalidGroupsHaveExplicitBehavior() {
        val channel = AnsightChannel(64, "Payment latency", group = "Checkout").validated()
        assertNull(channel.metricGroup)
        assertEquals("Checkout", channel.toJson().getString("group"))
        assertTrue(channel.toJson().isNull("metricGroup"))
        assertNull(AnsightMetricGroup.fromJson(null))
        assertThrows(IllegalArgumentException::class.java) { AnsightMetricGroup(" ", "Checkout") }
        assertThrows(IllegalArgumentException::class.java) { AnsightMetricGroup.fromJson(JSONObject("""{"id":"checkout","name":" "}""")) }
    }

    @Test
    fun builtInGroupIdentitiesArePlatformIndependent() {
        assertEquals("memory", AnsightMetricGroups.Memory.id)
        assertEquals("rendering", AnsightMetricGroups.Rendering.id)
        assertEquals("layout", AnsightMetricGroups.Layout.id)
        assertEquals("runtime", AnsightMetricGroups.Runtime.id)
        assertEquals("device", AnsightMetricGroups.Device.id)
        assertEquals("network", AnsightMetricGroups.Network.id)
    }
}
