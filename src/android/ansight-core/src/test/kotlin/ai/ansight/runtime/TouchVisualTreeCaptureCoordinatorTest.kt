package ai.ansight.runtime

import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class TouchVisualTreeCaptureCoordinatorTest {
    @Test
    fun gestureCapturesLeadingCheckpointAndTerminalTrees() {
        val triggers = LinkedBlockingQueue<TouchVisualTreeCaptureTrigger>()
        val coordinator = TouchVisualTreeCaptureCoordinator(
            capture = { trigger -> triggers.offer(trigger) },
            checkpointIntervalMilliseconds = 20,
        )

        coordinator.observe(createTouch("Down", pointerId = 7))
        val started = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected a leading capture.")
        val checkpoint = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected a gesture checkpoint.")
        coordinator.observe(createTouch("Up", pointerId = 7))
        var ended: TouchVisualTreeCaptureTrigger? = null
        for (attempt in 0 until 5) {
            val trigger = triggers.poll(1, TimeUnit.SECONDS) ?: break
            if (trigger.gesturePhase == TouchVisualTreeGesturePhase.Ended) {
                ended = trigger
                break
            }
        }
        coordinator.close()
        val terminal = ended ?: error("Expected a terminal capture.")

        assertEquals(TouchVisualTreeGesturePhase.Started, started.gesturePhase)
        assertEquals(TouchVisualTreeGesturePhase.Checkpoint, checkpoint.gesturePhase)
        assertEquals(TouchVisualTreeGesturePhase.Ended, terminal.gesturePhase)
        assertTrue(listOf(started, checkpoint, terminal).all { it.gestureId == started.gestureId })
    }

    private fun createTouch(action: String, pointerId: Long): CapturedTouch = CapturedTouch(
        action = action,
        pointerId = pointerId,
        pointerIndex = 0,
        pointerCount = 1,
        x = 24.0,
        y = 48.0,
        surfaceWidth = 200.0,
        surfaceHeight = 400.0,
        surfaceScale = 2.0,
    )
}
