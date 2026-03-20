package ai.ansight.runtime

import android.app.Application
import java.util.UUID

object AnsightRuntime {
    private val lock = Any()

    private var application: Application? = null
    private var options: AnsightOptions = AnsightOptions()
    private var initialized = false
    private var active = false
    private var sessionOpen = false
    private var sessionMessage: String? = null
    private val metrics = mutableListOf<RecordedMetric>()
    private val events = mutableListOf<RecordedEvent>()
    private val tools = linkedMapOf<String, AnsightToolDescriptor>()

    fun initialize(application: Application, options: AnsightOptions = AnsightOptions()) {
        synchronized(lock) {
            this.application = application
            this.options = options
            initialized = true
            sessionMessage = "Runtime initialized."
        }
    }

    fun activate() {
        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before activation." }
            active = true
            sessionMessage = "Runtime activated."
        }
    }

    fun deactivate() {
        synchronized(lock) {
            active = false
            sessionMessage = "Runtime deactivated."
        }
    }

    fun clear() {
        synchronized(lock) {
            metrics.clear()
            events.clear()
            sessionMessage = "Runtime buffers cleared."
        }
    }

    fun metric(value: Long, channel: Int = AnsightChannels.Unspecified) {
        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before recording metrics." }
            metrics += RecordedMetric(
                value = value,
                channel = validateChannel(channel),
                capturedAtEpochMs = System.currentTimeMillis(),
            )
            sessionMessage = "Recorded metric $value."
        }
    }

    fun event(
        label: String,
        type: AnsightEventType = AnsightEventType.Info,
        details: String? = null,
        channel: Int = AnsightChannels.Unspecified,
        id: String = UUID.randomUUID().toString(),
    ) {
        require(label.isNotBlank()) { "Event label must not be blank." }

        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before recording events." }
            events += RecordedEvent(
                id = id,
                label = label.trim(),
                type = type,
                details = details?.trim(),
                channel = validateChannel(channel),
                capturedAtEpochMs = System.currentTimeMillis(),
            )
            sessionMessage = "Recorded event ${label.trim()}."
        }
    }

    fun openSession(pairingJson: String, options: PairingOpenOptions): OpenSessionResult {
        synchronized(lock) {
            require(initialized) { "AnsightRuntime must be initialized before opening a session." }

            if (pairingJson.isBlank()) {
                return OpenSessionResult(false, "Pairing JSON is required.")
            }

            if (options.manualHostAddress.isBlank()) {
                return OpenSessionResult(false, "Manual host address is required.")
            }

            sessionOpen = true
            val sessionId = "android-${UUID.randomUUID()}"
            sessionMessage =
                "Harness session opened locally for ${options.clientName}. Network transport is not implemented yet."
            return OpenSessionResult(
                success = true,
                message = sessionMessage!!,
                sessionId = sessionId,
            )
        }
    }

    fun completeSession() {
        synchronized(lock) {
            sessionOpen = false
            sessionMessage = "Harness session completed locally."
        }
    }

    fun closeSession() {
        synchronized(lock) {
            sessionOpen = false
            sessionMessage = "Harness session closed."
        }
    }

    fun registerTool(tool: AnsightToolDescriptor) {
        require(tool.id.isNotBlank()) { "Tool id must not be blank." }

        synchronized(lock) {
            tools[tool.id] = tool
            sessionMessage = "Registered tool ${tool.id}."
        }
    }

    fun snapshot(): AnsightDebugSnapshot {
        synchronized(lock) {
            return AnsightDebugSnapshot(
                initialized = initialized,
                active = active,
                sessionOpen = sessionOpen,
                metricsRecorded = metrics.size,
                eventsRecorded = events.size,
                registeredTools = tools.size,
                lastMetric = metrics.lastOrNull(),
                lastEvent = events.lastOrNull(),
                sessionMessage = sessionMessage,
            )
        }
    }

    fun options(): AnsightOptions {
        synchronized(lock) {
            return options
        }
    }

    private fun validateChannel(channel: Int): Int {
        require(channel in 0..255) { "Channel ids must be between 0 and 255." }
        return channel
    }
}
