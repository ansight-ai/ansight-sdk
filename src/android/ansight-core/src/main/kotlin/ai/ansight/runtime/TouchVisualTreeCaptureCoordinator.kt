package ai.ansight.runtime

import java.util.UUID
import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors

internal enum class TouchVisualTreeGesturePhase(val wireName: String) {
    Started("started"),
    Checkpoint("checkpoint"),
    Ended("ended"),
}

internal data class TouchVisualTreeCaptureTrigger(
    val gestureId: String,
    val touchAction: String,
    val gesturePhase: TouchVisualTreeGesturePhase,
    val touchCapturedAtUtc: String,
)

internal class TouchVisualTreeCaptureCoordinator(
    private val capture: (TouchVisualTreeCaptureTrigger) -> Unit,
) : AutoCloseable {
    private val lock = Any()
    private val activePointerIds = mutableSetOf<Long>()
    private val executor: ExecutorService = Executors.newSingleThreadExecutor { runnable ->
        Thread(runnable, "AnsightTouchVisualTree").apply { isDaemon = true }
    }
    private var gestureId: String? = null
    private var closed = false

    fun observe(touch: CapturedTouch) {
        var trigger: TouchVisualTreeCaptureTrigger? = null
        synchronized(lock) {
            if (closed) {
                return
            }

            when (touch.action.lowercase()) {
                "down" -> {
                    val beginsGesture = activePointerIds.isEmpty()
                    activePointerIds += touch.pointerId
                    if (beginsGesture) {
                        gestureId = "gesture-${UUID.randomUUID()}"
                    }
                    trigger = createTrigger(
                        touch,
                        if (beginsGesture) {
                            TouchVisualTreeGesturePhase.Started
                        } else {
                            TouchVisualTreeGesturePhase.Checkpoint
                        },
                    )
                }
                "move" -> activePointerIds += touch.pointerId
                "up" -> {
                    activePointerIds -= touch.pointerId
                    val phase = if (activePointerIds.isEmpty()) {
                        TouchVisualTreeGesturePhase.Ended
                    } else {
                        TouchVisualTreeGesturePhase.Checkpoint
                    }
                    trigger = createTrigger(touch, phase)
                }
                "cancel" -> {
                    activePointerIds.clear()
                    gestureId = null
                }
            }
        }

        trigger?.let(::enqueue)
    }

    override fun close() {
        synchronized(lock) {
            if (closed) {
                return
            }
            closed = true
            activePointerIds.clear()
            gestureId = null
        }
        executor.shutdownNow()
    }

    fun interruptGesture() {
        synchronized(lock) {
            activePointerIds.clear()
            gestureId = null
        }
    }

    private fun enqueue(trigger: TouchVisualTreeCaptureTrigger) {
        runCatching {
            executor.execute { capture(trigger) }
        }
    }

    private fun createTrigger(
        touch: CapturedTouch,
        phase: TouchVisualTreeGesturePhase,
    ): TouchVisualTreeCaptureTrigger = TouchVisualTreeCaptureTrigger(
        gestureId = gestureId ?: "gesture-${UUID.randomUUID()}",
        touchAction = touch.action.lowercase(),
        gesturePhase = phase,
        touchCapturedAtUtc = touch.capturedAtUtc,
    )
}
