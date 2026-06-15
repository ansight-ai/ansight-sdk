package ai.ansight.runtime

import android.app.Activity
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.os.Handler
import android.os.Looper
import android.view.ActionMode
import android.view.KeyEvent
import android.view.Menu
import android.view.MenuItem
import android.view.MotionEvent
import android.view.SearchEvent
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.widget.TextView
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.lang.ref.WeakReference
import java.util.UUID
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

data class CapturedTouch(
    val id: String = UUID.randomUUID().toString(),
    val action: String,
    val pointerId: Long,
    val pointerIndex: Int,
    val pointerCount: Int,
    val x: Double,
    val y: Double,
    val surfaceWidth: Double?,
    val surfaceHeight: Double?,
    val coordinateUnit: String = "px",
    val surfaceScale: Double?,
    val capturedAtUtc: String = AnsightClock.isoNow(),
    val capturedAtEpochMs: Long = System.currentTimeMillis(),
) {
    val normalizedX: Double?
        get() = surfaceWidth?.takeIf { it > 0 }?.let { x / it }
    val normalizedY: Double?
        get() = surfaceHeight?.takeIf { it > 0 }?.let { y / it }
}

data class CapturedScreenshot(
    val bytes: ByteArray,
    val width: Int,
    val height: Int,
    val mimeType: String,
    val fileName: String,
)

object AndroidUiEvidence {
    private val mainHandler = Handler(Looper.getMainLooper())
    private var currentActivity = WeakReference<Activity>(null)
    private val callbackWrappers = mutableMapOf<Int, TouchWindowCallback>()
    private var touchHandler: ((CapturedTouch) -> Unit)? = null
    private val overlays = linkedMapOf<String, OverlaySpec>()

    fun onActivityResumed(activity: Activity) {
        currentActivity = WeakReference(activity)
        installTouchCallback(activity)
        attachOverlaySurface(activity)
    }

    fun onActivityDestroyed(activity: Activity) {
        val key = System.identityHashCode(activity.window)
        callbackWrappers.remove(key)
        if (currentActivity.get() === activity) {
            currentActivity = WeakReference(null)
        }
    }

    fun setTouchCaptureEnabled(enabled: Boolean, handler: ((CapturedTouch) -> Unit)?) {
        touchHandler = if (enabled) handler else null
        currentActivity.get()?.let(::installTouchCallback)
    }

    fun captureScreenshot(format: String = "jpeg", quality: Int = 80, maxWidth: Int? = null): CapturedScreenshot {
        return runOnMain {
            val activity = currentActivity.get() ?: error("No resumed Android activity is available for screenshot capture.")
            val root = activity.window.decorView.rootView ?: error("No Android root view is available for screenshot capture.")
            if (root.width <= 0 || root.height <= 0) {
                error("Android root view has no renderable size.")
            }

            val scale = maxWidth?.takeIf { it > 0 && root.width > it }?.let { it.toFloat() / root.width.toFloat() } ?: 1f
            val width = (root.width * scale).toInt().coerceAtLeast(1)
            val height = (root.height * scale).toInt().coerceAtLeast(1)
            val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            val canvas = Canvas(bitmap)
            if (scale != 1f) {
                canvas.scale(scale, scale)
            }
            root.draw(canvas)

            val normalizedFormat = format.trim().lowercase()
            val compressFormat = if (normalizedFormat == "png") Bitmap.CompressFormat.PNG else Bitmap.CompressFormat.JPEG
            val mimeType = if (compressFormat == Bitmap.CompressFormat.PNG) "image/png" else "image/jpeg"
            val extension = if (compressFormat == Bitmap.CompressFormat.PNG) "png" else "jpg"
            val output = ByteArrayOutputStream()
            bitmap.compress(compressFormat, quality.coerceIn(1, 100), output)
            bitmap.recycle()
            CapturedScreenshot(
                bytes = output.toByteArray(),
                width = width,
                height = height,
                mimeType = mimeType,
                fileName = "ansight-android-${System.currentTimeMillis()}.$extension",
            )
        }
    }

    fun visualTree(maxDepth: Int = 40, maxNodes: Int = 2_000): JSONObject {
        return runOnMain {
            val activity = currentActivity.get() ?: error("No resumed Android activity is available for visual tree capture.")
            val root = activity.window.decorView.rootView ?: error("No Android root view is available for visual tree capture.")
            val counter = NodeCounter(maxNodes.coerceAtLeast(1))
            JSONObject()
                .put("platform", "android")
                .put("adapter", "android.views")
                .put("capturedAtUtc", AnsightClock.isoNow())
                .put("activity", activity.javaClass.name)
                .put("root", serializeView(root, "0", 0, maxDepth.coerceAtLeast(1), counter))
                .put("truncated", counter.truncated)
                .put("nodeCount", counter.count)
        }
    }

    fun inspectNode(nodeId: String): JSONObject {
        val tree = visualTree()
        val root = tree.optJSONObject("root") ?: error("Visual tree did not contain a root node.")
        return findNode(root, nodeId.trim()) ?: error("Node '$nodeId' was not found.")
    }

    fun showOverlay(arguments: Map<String, String>): JSONObject {
        val id = arguments["id"]?.trim()?.ifBlank { null } ?: UUID.randomUUID().toString()
        val spec = OverlaySpec(
            id = id,
            label = arguments["label"]?.trim()?.ifBlank { null },
            x = arguments.doubleArg("x", 0.0).toFloat(),
            y = arguments.doubleArg("y", 0.0).toFloat(),
            width = arguments.doubleArg("width", 120.0).toFloat().coerceAtLeast(1f),
            height = arguments.doubleArg("height", 64.0).toFloat().coerceAtLeast(1f),
            color = arguments["color"]?.toColorOrNull() ?: Color.argb(180, 0, 122, 255),
        )
        overlays[id] = spec
        redrawOverlays()
        return overlayJson(spec)
    }

    fun getOverlay(id: String): JSONObject {
        return overlayJson(overlays[id.trim()] ?: error("Overlay '${id.trim()}' was not found."))
    }

    fun queryOverlays(): JSONObject = JSONObject()
        .put("overlays", JSONArray(overlays.values.map(::overlayJson)))
        .put("count", overlays.size)

    fun updateOverlay(arguments: Map<String, String>): JSONObject {
        val id = arguments["id"]?.trim()?.ifBlank { null } ?: error("Overlay id is required.")
        val current = overlays[id] ?: error("Overlay '$id' was not found.")
        val updated = current.copy(
            label = arguments["label"]?.trim()?.ifBlank { null } ?: current.label,
            x = arguments["x"]?.toDoubleOrNull()?.toFloat() ?: current.x,
            y = arguments["y"]?.toDoubleOrNull()?.toFloat() ?: current.y,
            width = arguments["width"]?.toDoubleOrNull()?.toFloat()?.coerceAtLeast(1f) ?: current.width,
            height = arguments["height"]?.toDoubleOrNull()?.toFloat()?.coerceAtLeast(1f) ?: current.height,
            color = arguments["color"]?.toColorOrNull() ?: current.color,
        )
        overlays[id] = updated
        redrawOverlays()
        return overlayJson(updated)
    }

    fun removeOverlay(id: String): JSONObject {
        val removed = overlays.remove(id.trim()) ?: error("Overlay '${id.trim()}' was not found.")
        redrawOverlays()
        return overlayJson(removed).put("removed", true)
    }

    fun clearOverlays(): JSONObject {
        val count = overlays.size
        overlays.clear()
        redrawOverlays()
        return JSONObject().put("removedCount", count)
    }

    private fun installTouchCallback(activity: Activity) {
        if (touchHandler == null) {
            return
        }

        val window = activity.window ?: return
        val key = System.identityHashCode(window)
        val existing = callbackWrappers[key]
        if (existing != null && existing.activity.get() === activity) {
            return
        }

        val original = window.callback ?: return
        if (original is TouchWindowCallback) {
            callbackWrappers[key] = original
            return
        }

        val wrapper = TouchWindowCallback(activity, original) { event ->
            captureTouch(activity, event)
        }
        callbackWrappers[key] = wrapper
        window.callback = wrapper
    }

    private fun captureTouch(activity: Activity, event: MotionEvent) {
        val handler = touchHandler ?: return
        val actionIndex = event.actionIndex.coerceIn(0, (event.pointerCount - 1).coerceAtLeast(0))
        val action = when (event.actionMasked) {
            MotionEvent.ACTION_DOWN,
            MotionEvent.ACTION_POINTER_DOWN -> "Down"
            MotionEvent.ACTION_MOVE -> "Move"
            MotionEvent.ACTION_UP,
            MotionEvent.ACTION_POINTER_UP -> "Up"
            MotionEvent.ACTION_CANCEL -> "Cancel"
            else -> "Unknown"
        }
        val root = activity.window.decorView.rootView
        if (event.actionMasked == MotionEvent.ACTION_MOVE) {
            for (index in 0 until event.pointerCount) {
                handler(touchFromEvent(event, index, action, root))
            }
        } else {
            handler(touchFromEvent(event, actionIndex, action, root))
        }
    }

    private fun touchFromEvent(event: MotionEvent, index: Int, action: String, root: View): CapturedTouch =
        CapturedTouch(
            action = action,
            pointerId = event.getPointerId(index).toLong(),
            pointerIndex = index,
            pointerCount = event.pointerCount,
            x = event.getX(index).toDouble(),
            y = event.getY(index).toDouble(),
            surfaceWidth = root.width.takeIf { it > 0 }?.toDouble(),
            surfaceHeight = root.height.takeIf { it > 0 }?.toDouble(),
            surfaceScale = root.resources.displayMetrics.density.toDouble(),
        )

    private fun serializeView(view: View, nodeId: String, depth: Int, maxDepth: Int, counter: NodeCounter): JSONObject {
        if (!counter.tryEnter()) {
            return JSONObject()
                .put("id", nodeId)
                .put("type", view.javaClass.name)
                .put("truncated", true)
        }

        val location = IntArray(2)
        runCatching { view.getLocationOnScreen(location) }
        val json = JSONObject()
            .put("id", nodeId)
            .put("type", view.javaClass.name)
            .put("resourceId", view.resourceNameOrNull())
            .put("text", (view as? TextView)?.text?.toString())
            .put("contentDescription", view.contentDescription?.toString())
            .put("visible", view.visibility == View.VISIBLE)
            .put("visibility", visibilityName(view.visibility))
            .put("enabled", view.isEnabled)
            .put("focused", view.isFocused)
            .put("clickable", view.isClickable)
            .put("bounds", JSONObject()
                .put("x", location[0])
                .put("y", location[1])
                .put("width", view.width)
                .put("height", view.height))
            .put("alpha", view.alpha.toDouble())
            .put("importantForAccessibility", view.importantForAccessibility)

        if (view is ViewGroup && depth < maxDepth) {
            val children = JSONArray()
            for (index in 0 until view.childCount) {
                children.put(serializeView(view.getChildAt(index), "$nodeId.$index", depth + 1, maxDepth, counter))
            }
            json.put("children", children)
        } else {
            json.put("children", JSONArray())
        }
        return json
    }

    private fun View.resourceNameOrNull(): String? {
        if (id == View.NO_ID) {
            return null
        }
        return try {
            resources.getResourceName(id)
        } catch (_: Exception) {
            id.toString()
        }
    }

    private fun findNode(node: JSONObject, nodeId: String): JSONObject? {
        if (node.optString("id") == nodeId) {
            return node
        }
        val children = node.optJSONArray("children") ?: return null
        for (index in 0 until children.length()) {
            val child = children.optJSONObject(index) ?: continue
            findNode(child, nodeId)?.let { return it }
        }
        return null
    }

    private fun attachOverlaySurface(activity: Activity) {
        runOnMain {
            val decor = activity.window.decorView as? ViewGroup ?: return@runOnMain
            if (decor.findViewWithTag<View>("ansight.overlay.surface") == null) {
                val surface = OverlaySurface(activity)
                surface.tag = "ansight.overlay.surface"
                decor.addView(
                    surface,
                    ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT),
                )
            }
            redrawOverlays()
        }
    }

    private fun redrawOverlays() {
        runCatching {
            runOnMain {
                val decor = currentActivity.get()?.window?.decorView as? ViewGroup ?: return@runOnMain
                val surface = decor.findViewWithTag<OverlaySurface>("ansight.overlay.surface") ?: return@runOnMain
                surface.setOverlays(overlays.values.toList())
            }
        }
    }

    private fun overlayJson(spec: OverlaySpec): JSONObject = JSONObject()
        .put("id", spec.id)
        .putNullable("label", spec.label)
        .put("x", spec.x.toDouble())
        .put("y", spec.y.toDouble())
        .put("width", spec.width.toDouble())
        .put("height", spec.height.toDouble())
        .put("color", "#${Integer.toHexString(spec.color).padStart(8, '0')}")

    private fun visibilityName(visibility: Int): String = when (visibility) {
        View.VISIBLE -> "visible"
        View.INVISIBLE -> "invisible"
        View.GONE -> "gone"
        else -> visibility.toString()
    }

    private fun <T> runOnMain(block: () -> T): T {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            return block()
        }
        var result: Result<T>? = null
        val latch = CountDownLatch(1)
        mainHandler.post {
            result = runCatching(block)
            latch.countDown()
        }
        if (!latch.await(5, TimeUnit.SECONDS)) {
            error("Timed out waiting for Android main thread.")
        }
        return result!!.getOrThrow()
    }

    private data class OverlaySpec(
        val id: String,
        val label: String?,
        val x: Float,
        val y: Float,
        val width: Float,
        val height: Float,
        val color: Int,
    )

    private class NodeCounter(private val maxNodes: Int) {
        var count = 0
            private set
        var truncated = false
            private set

        fun tryEnter(): Boolean {
            if (count >= maxNodes) {
                truncated = true
                return false
            }
            count += 1
            return true
        }
    }

    private class OverlaySurface(activity: Activity) : View(activity) {
        private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
        private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            color = Color.WHITE
            textSize = 28f
        }
        private var specs: List<OverlaySpec> = emptyList()

        init {
            setWillNotDraw(false)
            isClickable = false
            isFocusable = false
            importantForAccessibility = IMPORTANT_FOR_ACCESSIBILITY_NO
        }

        fun setOverlays(specs: List<OverlaySpec>) {
            this.specs = specs
            invalidate()
        }

        override fun onDraw(canvas: Canvas) {
            super.onDraw(canvas)
            specs.forEach { spec ->
                paint.style = Paint.Style.FILL
                paint.color = spec.color
                val rect = Rect(spec.x.toInt(), spec.y.toInt(), (spec.x + spec.width).toInt(), (spec.y + spec.height).toInt())
                canvas.drawRect(rect, paint)
                paint.style = Paint.Style.STROKE
                paint.strokeWidth = 3f
                paint.color = Color.WHITE
                canvas.drawRect(rect, paint)
                spec.label?.let { canvas.drawText(it.take(80), spec.x + 8f, spec.y + 32f, textPaint) }
            }
        }
    }

    private class TouchWindowCallback(
        val activity: WeakReference<Activity>,
        private val delegate: Window.Callback,
        private val touchHandler: (MotionEvent) -> Unit,
    ) : Window.Callback {
        constructor(activity: Activity, delegate: Window.Callback, touchHandler: (MotionEvent) -> Unit) :
            this(WeakReference(activity), delegate, touchHandler)

        override fun dispatchKeyEvent(event: KeyEvent): Boolean = delegate.dispatchKeyEvent(event)
        override fun dispatchKeyShortcutEvent(event: KeyEvent): Boolean = delegate.dispatchKeyShortcutEvent(event)
        override fun dispatchTouchEvent(event: MotionEvent): Boolean {
            touchHandler(event)
            return delegate.dispatchTouchEvent(event)
        }
        override fun dispatchTrackballEvent(event: MotionEvent): Boolean = delegate.dispatchTrackballEvent(event)
        override fun dispatchGenericMotionEvent(event: MotionEvent): Boolean = delegate.dispatchGenericMotionEvent(event)
        override fun dispatchPopulateAccessibilityEvent(event: AccessibilityEvent): Boolean = delegate.dispatchPopulateAccessibilityEvent(event)
        override fun onCreatePanelView(featureId: Int): View? = delegate.onCreatePanelView(featureId)
        override fun onCreatePanelMenu(featureId: Int, menu: Menu): Boolean = delegate.onCreatePanelMenu(featureId, menu)
        override fun onPreparePanel(featureId: Int, view: View?, menu: Menu): Boolean = delegate.onPreparePanel(featureId, view, menu)
        override fun onMenuOpened(featureId: Int, menu: Menu): Boolean = delegate.onMenuOpened(featureId, menu)
        override fun onMenuItemSelected(featureId: Int, item: MenuItem): Boolean = delegate.onMenuItemSelected(featureId, item)
        override fun onWindowAttributesChanged(attrs: WindowManager.LayoutParams) = delegate.onWindowAttributesChanged(attrs)
        override fun onContentChanged() = delegate.onContentChanged()
        override fun onWindowFocusChanged(hasFocus: Boolean) = delegate.onWindowFocusChanged(hasFocus)
        override fun onAttachedToWindow() = delegate.onAttachedToWindow()
        override fun onDetachedFromWindow() = delegate.onDetachedFromWindow()
        override fun onPanelClosed(featureId: Int, menu: Menu) = delegate.onPanelClosed(featureId, menu)
        override fun onSearchRequested(): Boolean = delegate.onSearchRequested()
        override fun onSearchRequested(searchEvent: SearchEvent): Boolean = delegate.onSearchRequested(searchEvent)
        override fun onWindowStartingActionMode(callback: ActionMode.Callback): ActionMode? = delegate.onWindowStartingActionMode(callback)
        override fun onWindowStartingActionMode(callback: ActionMode.Callback, type: Int): ActionMode? =
            delegate.onWindowStartingActionMode(callback, type)
        override fun onActionModeStarted(mode: ActionMode) = delegate.onActionModeStarted(mode)
        override fun onActionModeFinished(mode: ActionMode) = delegate.onActionModeFinished(mode)
    }
}

private fun Map<String, String>.doubleArg(name: String, defaultValue: Double): Double =
    this[name]?.toDoubleOrNull() ?: defaultValue

private fun String.toColorOrNull(): Int? = try {
    Color.parseColor(this)
} catch (_: Exception) {
    null
}
