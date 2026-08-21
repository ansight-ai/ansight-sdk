package ai.ansight.runtime

import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class TouchVisualTreeCaptureCoordinatorTest {
    @Test
    fun gestureCapturesOnlyDownAndUpTrees() {
        val triggers = LinkedBlockingQueue<TouchVisualTreeCaptureTrigger>()
        val coordinator = TouchVisualTreeCaptureCoordinator(
            capture = { trigger -> triggers.offer(trigger) },
        )

        coordinator.observe(createTouch("Down", pointerId = 7))
        val started = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected a leading capture.")
        coordinator.observe(createTouch("Move", pointerId = 7))
        assertNull(triggers.poll(350, TimeUnit.MILLISECONDS))
        coordinator.observe(createTouch("Up", pointerId = 7))
        val ended = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected a terminal capture.")
        coordinator.close()

        assertEquals(TouchVisualTreeGesturePhase.Started, started.gesturePhase)
        assertEquals("down", started.touchAction)
        assertEquals(TouchVisualTreeGesturePhase.Ended, ended.gesturePhase)
        assertEquals("up", ended.touchAction)
        assertEquals(started.gestureId, ended.gestureId)
        assertTrue(triggers.isEmpty())
    }

    @Test
    fun cancelDoesNotCaptureTree() {
        val triggers = LinkedBlockingQueue<TouchVisualTreeCaptureTrigger>()
        val coordinator = TouchVisualTreeCaptureCoordinator(
            capture = { trigger -> triggers.offer(trigger) },
        )

        coordinator.observe(createTouch("Down", pointerId = 7))
        val started = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected a leading capture.")
        coordinator.observe(createTouch("Cancel", pointerId = 7))
        assertNull(triggers.poll(100, TimeUnit.MILLISECONDS))
        coordinator.close()

        assertEquals(TouchVisualTreeGesturePhase.Started, started.gesturePhase)
        assertEquals("down", started.touchAction)
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
