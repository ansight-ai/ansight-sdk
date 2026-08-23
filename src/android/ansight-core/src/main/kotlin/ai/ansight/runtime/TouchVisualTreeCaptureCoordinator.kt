package ai.ansight.runtime

import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledExecutorService
import java.util.concurrent.TimeUnit

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
    private val minimumCaptureIntervalMilliseconds: Long = DefaultMinimumCaptureIntervalMilliseconds,
) : AutoCloseable {
    init {
        require(minimumCaptureIntervalMilliseconds > 0)
    }

    private val lock = Any()
    private val activePointerIds = mutableSetOf<Long>()
    private val executor: ScheduledExecutorService = Executors.newSingleThreadScheduledExecutor { runnable ->
        Thread(runnable, "AnsightTouchVisualTree").apply { isDaemon = true }
    }
    private var pendingTrigger: TouchVisualTreeCaptureTrigger? = null
    private var workerScheduled = false
    private var nextCaptureAllowedAtNanoseconds = 0L
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
            pendingTrigger = null
            gestureId = null
        }
        executor.shutdownNow()
    }

    fun interruptGesture() {
        synchronized(lock) {
            activePointerIds.clear()
            pendingTrigger = null
            gestureId = null
        }
    }

    private fun enqueue(trigger: TouchVisualTreeCaptureTrigger) {
        var scheduleDelayMilliseconds = -1L
        synchronized(lock) {
            if (closed) {
                return
            }

            pendingTrigger = selectPendingTrigger(pendingTrigger, trigger)
            if (!workerScheduled) {
                workerScheduled = true
                scheduleDelayMilliseconds = remainingCaptureDelayMilliseconds()
            }
        }

        if (scheduleDelayMilliseconds >= 0) {
            scheduleWorker(scheduleDelayMilliseconds)
        }
    }

    private fun scheduleWorker(delayMilliseconds: Long) {
        runCatching {
            executor.schedule(
                { drainPendingTrigger() },
                delayMilliseconds,
                TimeUnit.MILLISECONDS,
            )
        }.onFailure {
            synchronized(lock) {
                workerScheduled = false
            }
        }
    }

    private fun drainPendingTrigger() {
        val trigger = synchronized(lock) {
            if (closed) {
                workerScheduled = false
                return
            }

            pendingTrigger.also { pendingTrigger = null }
        }

        if (trigger == null) {
            synchronized(lock) {
                workerScheduled = false
            }
            return
        }

        runCatching { capture(trigger) }
        synchronized(lock) {
            nextCaptureAllowedAtNanoseconds = System.nanoTime() +
                TimeUnit.MILLISECONDS.toNanos(minimumCaptureIntervalMilliseconds)
        }

        val shouldContinue = synchronized(lock) {
            if (closed || pendingTrigger == null) {
                workerScheduled = false
                false
            } else {
                true
            }
        }
        if (shouldContinue) {
            scheduleWorker(remainingCaptureDelayMilliseconds())
        }
    }

    private fun remainingCaptureDelayMilliseconds(): Long {
        val remainingNanoseconds = synchronized(lock) {
            nextCaptureAllowedAtNanoseconds - System.nanoTime()
        }
        return if (remainingNanoseconds <= 0) {
            0
        } else {
            TimeUnit.NANOSECONDS.toMillis(remainingNanoseconds - 1) + 1
        }
    }

    private fun selectPendingTrigger(
        pending: TouchVisualTreeCaptureTrigger?,
        incoming: TouchVisualTreeCaptureTrigger,
    ): TouchVisualTreeCaptureTrigger {
        if (pending == null || incoming.gesturePhase == TouchVisualTreeGesturePhase.Started) {
            return incoming
        }
        if (pending.gesturePhase == TouchVisualTreeGesturePhase.Started) {
            return pending
        }
        return if (incoming.gesturePhase == TouchVisualTreeGesturePhase.Ended) incoming else pending
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

    companion object {
        const val DefaultMinimumCaptureIntervalMilliseconds = 750L
    }
}
