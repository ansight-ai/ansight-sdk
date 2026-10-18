package ai.ansight.runtime

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class TelemetryContractTest {
    @Test fun fractionalSamplerAndSpanIdentity() {
        val channel = AnsightChannel(64, "Frame", unit = "ms", group = "Render")
        val stream = AnsightMetricStream(channel) { 16.67 }
        assertEquals(16.67, stream.sample()!!.toDouble(), 0.0)
        val events = mutableListOf<JSONObject>()
        val span = AnsightSpan("Load", 64, "parent") { _, type, details, id ->
            assertEquals(AnsightEventType.Span, type); assertEquals(64, id); events += JSONObject(details)
        }
        span.fail(); span.close(); span.end()
        assertEquals(2, events.size)
        assertEquals(span.id, events[1].getString("spanId"))
        assertEquals("parent", events[1].getString("parentSpanId"))
        assertEquals("error", events[1].getString("status"))
        assertTrue(events[1].getDouble("durationMs") >= 0)
    }
}
