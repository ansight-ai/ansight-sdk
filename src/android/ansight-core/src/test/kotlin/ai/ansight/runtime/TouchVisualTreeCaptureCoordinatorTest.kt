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
            minimumCaptureIntervalMilliseconds = 10,
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
            minimumCaptureIntervalMilliseconds = 10,
        )

        coordinator.observe(createTouch("Down", pointerId = 7))
        val started = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected a leading capture.")
        coordinator.observe(createTouch("Cancel", pointerId = 7))
        assertNull(triggers.poll(100, TimeUnit.MILLISECONDS))
        coordinator.close()

        assertEquals(TouchVisualTreeGesturePhase.Started, started.gesturePhase)
        assertEquals("down", started.touchAction)
    }

    @Test
    fun busyCaptureCoalescesAContinuousGestureBurst() {
        val triggers = LinkedBlockingQueue<TouchVisualTreeCaptureTrigger>()
        val firstCaptureStarted = java.util.concurrent.CountDownLatch(1)
        val releaseFirstCapture = java.util.concurrent.CountDownLatch(1)
        val coordinator = TouchVisualTreeCaptureCoordinator(
            capture = { trigger ->
                triggers.offer(trigger)
                if (triggers.size == 1) {
                    firstCaptureStarted.countDown()
                    releaseFirstCapture.await(1, TimeUnit.SECONDS)
                }
            },
            minimumCaptureIntervalMilliseconds = 20,
        )

        coordinator.observe(createTouch("Down", pointerId = 1))
        assertTrue(firstCaptureStarted.await(1, TimeUnit.SECONDS))
        coordinator.observe(createTouch("Up", pointerId = 1))
        for (pointerId in 2L..20L) {
            coordinator.observe(createTouch("Down", pointerId))
            coordinator.observe(createTouch("Up", pointerId))
        }

        releaseFirstCapture.countDown()
        val first = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected the leading capture.")
        val second = triggers.poll(1, TimeUnit.SECONDS) ?: error("Expected the coalesced capture.")
        assertNull(triggers.poll(100, TimeUnit.MILLISECONDS))
        coordinator.close()

        assertEquals(TouchVisualTreeGesturePhase.Started, first.gesturePhase)
        assertEquals(TouchVisualTreeGesturePhase.Started, second.gesturePhase)
        assertTrue(first.gestureId != second.gestureId)
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
