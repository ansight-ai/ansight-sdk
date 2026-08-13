package ai.ansight.runtime

import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit

internal enum class TouchVisualTreeGesturePhase(val wireName: String) {
    Started("started"),
    Checkpoint("checkpoint"),
    Ended("ended"),
    Cancelled("cancelled"),
}

internal data class TouchVisualTreeCaptureTrigger(
    val gestureId: String,
    val touchAction: String,
    val gesturePhase: TouchVisualTreeGesturePhase,
    val touchCapturedAtUtc: String,
)

internal class TouchVisualTreeCaptureCoordinator(
    private val capture: (TouchVisualTreeCaptureTrigger) -> Unit,
    private val checkpointIntervalMilliseconds: Long = DefaultCheckpointIntervalMilliseconds,
) : AutoCloseable {
    companion object {
        const val DefaultCheckpointIntervalMilliseconds = 250L
    }

    private val lock = Any()
    private val activePointerIds = mutableSetOf<Long>()
    private val executor: ScheduledExecutorService = Executors.newSingleThreadScheduledExecutor { runnable ->
        Thread(runnable, "AnsightTouchVisualTree").apply { isDaemon = true }
    }
    private var checkpointTask: ScheduledFuture<*>? = null
    private var latestTouch: CapturedTouch? = null
    private var gestureId: String? = null
    private var closed = false

    fun observe(touch: CapturedTouch) {
        var trigger: TouchVisualTreeCaptureTrigger? = null
        synchronized(lock) {
            if (closed) {
                return
            }

            latestTouch = touch
            when (touch.action.lowercase()) {
                "down" -> {
                    val beginsGesture = activePointerIds.isEmpty()
                    activePointerIds += touch.pointerId
                    if (beginsGesture) {
                        gestureId = "gesture-${UUID.randomUUID()}"
                        startCheckpointTaskLocked()
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
                        stopCheckpointTaskLocked()
                        TouchVisualTreeGesturePhase.Ended
                    } else {
                        TouchVisualTreeGesturePhase.Checkpoint
                    }
                    trigger = createTrigger(touch, phase)
                }
                "cancel" -> {
                    activePointerIds.clear()
                    stopCheckpointTaskLocked()
                    trigger = createTrigger(touch, TouchVisualTreeGesturePhase.Cancelled)
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
            latestTouch = null
            gestureId = null
            stopCheckpointTaskLocked()
        }
        executor.shutdownNow()
    }

    fun interruptGesture() {
        synchronized(lock) {
            activePointerIds.clear()
            latestTouch = null
            gestureId = null
            stopCheckpointTaskLocked()
        }
    }

    private fun startCheckpointTaskLocked() {
        stopCheckpointTaskLocked()
        checkpointTask = executor.scheduleWithFixedDelay(
            { captureCheckpointIfActive() },
            checkpointIntervalMilliseconds,
            checkpointIntervalMilliseconds,
            TimeUnit.MILLISECONDS,
        )
    }

    private fun stopCheckpointTaskLocked() {
        checkpointTask?.cancel(false)
        checkpointTask = null
    }

    private fun captureCheckpointIfActive() {
        val trigger = synchronized(lock) {
            if (closed || activePointerIds.isEmpty()) {
                null
            } else {
                latestTouch?.let { createTrigger(it, TouchVisualTreeGesturePhase.Checkpoint) }
            }
        }
        trigger?.let(capture)
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
