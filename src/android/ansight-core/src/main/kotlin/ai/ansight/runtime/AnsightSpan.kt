package ai.ansight.runtime

import org.json.JSONObject
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

/** A timed operation (zone). Parent identity is explicit so it survives async/thread boundaries. */
class AnsightSpan internal constructor(
    val name: String,
    val channel: Int,
    val parentSpanId: String?,
    private val emit: (String, AnsightEventType, String, Int) -> Unit,
) : AutoCloseable {
    val id: String = UUID.randomUUID().toString()
    private val startedAtUtc = AnsightClock.isoNow()
    private val startedNanos = System.nanoTime()
    private val ended = AtomicBoolean(false)

    init {
        require(name.isNotBlank()) { "Span name must not be blank." }
        require(channel in 0..255) { "Channel ids must be between 0 and 255." }
        publish("start", null, null)
    }

    @JvmOverloads
    fun end(status: String = "ok") {
        require(status in setOf("ok", "error", "cancelled")) { "Span status must be ok, error, or cancelled." }
        if (ended.compareAndSet(false, true)) {
            publish("end", (System.nanoTime() - startedNanos) / 1_000_000.0, status)
        }
    }

    fun fail() = end("error")
    override fun close() = end()

    private fun publish(phase: String, durationMs: Double?, status: String?) {
        val details = JSONObject().put("spanId", id).put("parentSpanId", parentSpanId)
            .put("phase", phase).put("startedAtUtc", startedAtUtc)
            .put("durationMs", durationMs).put("status", status)
        emit(name, AnsightEventType.Span, details.toString(), channel)
    }
}
