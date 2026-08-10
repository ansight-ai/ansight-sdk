package ai.ansight.runtime

import android.app.Activity
import android.app.Application
import android.content.Context
import android.content.ContextWrapper
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.drawable.ColorDrawable
import android.os.Build
import android.os.Handler
import android.os.HandlerThread
import android.os.Looper
import android.view.ActionMode
import android.view.KeyEvent
import android.view.Menu
import android.view.MenuItem
import android.view.MotionEvent
import android.view.PixelCopy
import android.view.SearchEvent
import android.view.SurfaceView
import android.view.TextureView
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.view.WindowManager
import android.view.accessibility.AccessibilityEvent
import android.text.method.PasswordTransformationMethod
import android.widget.CompoundButton
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.HorizontalScrollView
import android.widget.RadioButton
import android.widget.SeekBar
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.lang.ref.WeakReference
import java.util.Locale
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
    private val pixelCopyThread = HandlerThread("AnsightAndroidPixelCopy").apply { start() }
    private val pixelCopyHandler = Handler(pixelCopyThread.looper)
    private var currentActivity = WeakReference<Activity>(null)
    private val callbackWrappers = mutableMapOf<Int, TouchWindowCallback>()
    private var touchHandler: ((CapturedTouch) -> Unit)? = null
    private val overlays = linkedMapOf<String, OverlaySpec>()
    private var streamBitmap: Bitmap? = null
    private var streamBitmapWidth: Int = 0
    private var streamBitmapHeight: Int = 0

    fun onActivityResumed(activity: Activity) {
        runOnMain {
            bindActivity(activity)
        }
    }

    fun onActivityDestroyed(activity: Activity) {
        val key = System.identityHashCode(activity.window)
        callbackWrappers.remove(key)
        if (currentActivity.get() === activity) {
            releaseSessionScreenshotResources()
            currentActivity = WeakReference(null)
        }
    }

    fun setTouchCaptureEnabled(enabled: Boolean, handler: ((CapturedTouch) -> Unit)?) {
        touchHandler = if (enabled) handler else null
        runOnMain {
            currentActivity.get()?.let { activity -> installTouchCallback(activity) }
        }
    }

    fun currentActivity(): Activity? = currentActivity.get()

    fun bindCurrentActivity(application: Application? = null): Activity? {
        return runOnMain {
            val activity = currentActivity.get()?.takeIf { it.isUsableFor(application) }
                ?: reflectedWindowActivity(application)
                ?: reflectedActivityThreadActivity(application)
                ?: return@runOnMain null
            bindActivity(activity)
            activity
        }
    }

    fun captureScreenshot(
        format: String = "jpeg",
        quality: Int = AnsightSessionJpegCaptureOptions.DefaultQuality,
        maxWidth: Int? = AnsightSessionJpegCaptureOptions.DefaultMaxWidth,
    ): CapturedScreenshot {
        return captureScreenshot(format, quality, maxWidth, reuseStreamBitmap = false)
    }

    fun captureSessionScreenshot(
        format: String = "jpeg",
        quality: Int = AnsightSessionJpegCaptureOptions.DefaultQuality,
        maxWidth: Int? = AnsightSessionJpegCaptureOptions.DefaultMaxWidth,
    ): CapturedScreenshot {
        return captureScreenshot(format, quality, maxWidth, reuseStreamBitmap = true)
    }

    fun releaseSessionScreenshotResources() {
        runOnMain {
            streamBitmap?.recycle()
            streamBitmap = null
            streamBitmapWidth = 0
            streamBitmapHeight = 0
        }
    }

    private fun captureScreenshot(format: String, quality: Int, maxWidth: Int?, reuseStreamBitmap: Boolean): CapturedScreenshot {
        return runOnMain {
            val activity = currentActivity.get() ?: bindCurrentActivity() ?: error("No resumed Android activity is available for screenshot capture.")
            val activityRoot = activity.window.decorView.rootView ?: error("No Android root view is available for screenshot capture.")
            if (activityRoot.width <= 0 || activityRoot.height <= 0) {
                error("Android root view has no renderable size.")
            }

            val windows = captureTargets(activity)
            val activityBounds = windows.firstOrNull { it.isActivityWindow }?.boundsInScreen ?: activityRoot.boundsInScreen()
            val scale = maxWidth?.takeIf { it > 0 && activityBounds.width() > it }?.let { it.toFloat() / activityBounds.width().toFloat() } ?: 1f
            val width = (activityBounds.width() * scale).toInt().coerceAtLeast(1)
            val height = (activityBounds.height() * scale).toInt().coerceAtLeast(1)
            val bitmap = if (reuseStreamBitmap) {
                streamBitmap?.takeIf { !it.isRecycled && streamBitmapWidth == width && streamBitmapHeight == height }
                    ?: Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).also {
                        streamBitmap?.recycle()
                        streamBitmap = it
                        streamBitmapWidth = width
                        streamBitmapHeight = height
                    }
            } else {
                Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
            }
            bitmap.eraseColor(Color.TRANSPARENT)
            val canvas = Canvas(bitmap)
            val activityWindowCopied = copyActivityWindowPixels(activity.window, bitmap)
            if (!activityWindowCopied) {
                windows.forEach { target ->
                    drawWindowTarget(canvas, target, activityBounds, scale, copyGpuBackedChildren = true)
                }
            } else {
                windows.forEach { target ->
                    if (target.isActivityWindow) {
                        drawGpuBackedChildren(canvas, target.view, activityBounds, scale)
                    } else {
                        drawWindowTarget(canvas, target, activityBounds, scale, copyGpuBackedChildren = true)
                    }
                }
            }

            try {
                val normalizedFormat = format.trim().lowercase(Locale.US)
                val compressFormat = if (normalizedFormat == "png") Bitmap.CompressFormat.PNG else Bitmap.CompressFormat.JPEG
                val mimeType = if (compressFormat == Bitmap.CompressFormat.PNG) "image/png" else "image/jpeg"
                val extension = if (compressFormat == Bitmap.CompressFormat.PNG) "png" else "jpg"
                val output = ByteArrayOutputStream()
                bitmap.compress(compressFormat, quality.coerceIn(1, 100), output)
                CapturedScreenshot(
                    bytes = output.toByteArray(),
                    width = width,
                    height = height,
                    mimeType = mimeType,
                    fileName = "ansight-android-${System.currentTimeMillis()}.$extension",
                )
            } finally {
                if (!reuseStreamBitmap) {
                    bitmap.recycle()
                }
            }
        }
    }

    private fun copyActivityWindowPixels(window: Window, destination: Bitmap): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) {
            return false
        }

        val latch = CountDownLatch(1)
        var copied = false
        return try {
            PixelCopy.request(window, destination, { result ->
                copied = result == PixelCopy.SUCCESS
                latch.countDown()
            }, pixelCopyHandler)
            latch.await(500, TimeUnit.MILLISECONDS) && copied
        } catch (_: Exception) {
            false
        }
    }

    private fun drawWindowTarget(
        canvas: Canvas,
        target: WindowCaptureTarget,
        activityBounds: Rect,
        scale: Float,
        copyGpuBackedChildren: Boolean,
    ) {
        if (target.boundsInScreen.width() <= 0 || target.boundsInScreen.height() <= 0) {
            return
        }

        canvas.save()
        try {
            canvas.scale(scale, scale)
            canvas.clipRect(0, 0, activityBounds.width(), activityBounds.height())
            canvas.translate(
                (target.boundsInScreen.left - activityBounds.left).toFloat(),
                (target.boundsInScreen.top - activityBounds.top).toFloat(),
            )
            target.view.draw(canvas)
        } finally {
            canvas.restore()
        }

        if (copyGpuBackedChildren) {
            drawGpuBackedChildren(canvas, target.view, activityBounds, scale)
        }
    }

    private fun drawGpuBackedChildren(canvas: Canvas, root: View, activityBounds: Rect, scale: Float) {
        root.forEachDescendant { view ->
            if (!view.isRenderable()) {
                return@forEachDescendant
            }

            val capture = when (view) {
                is SurfaceView -> copySurfaceView(view, scale)
                is TextureView -> copyTextureView(view, scale)
                else -> null
            } ?: return@forEachDescendant

            val bounds = view.boundsInScreen()
            val destination = RectF(
                (bounds.left - activityBounds.left) * scale,
                (bounds.top - activityBounds.top) * scale,
                (bounds.right - activityBounds.left) * scale,
                (bounds.bottom - activityBounds.top) * scale,
            )
            canvas.drawBitmap(capture, null, destination, null)
            capture.recycle()
        }
    }

    private fun copySurfaceView(surfaceView: SurfaceView, scale: Float): Bitmap? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O || !surfaceView.holder.surface.isValid) {
            return null
        }
        val width = (surfaceView.width * scale).toInt().coerceAtLeast(1)
        val height = (surfaceView.height * scale).toInt().coerceAtLeast(1)
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val latch = CountDownLatch(1)
        var copied = false
        return try {
            PixelCopy.request(surfaceView, bitmap, { result ->
                copied = result == PixelCopy.SUCCESS
                latch.countDown()
            }, pixelCopyHandler)
            if (latch.await(500, TimeUnit.MILLISECONDS) && copied) {
                bitmap
            } else {
                bitmap.recycle()
                null
            }
        } catch (_: Exception) {
            bitmap.recycle()
            null
        }
    }

    private fun copyTextureView(textureView: TextureView, scale: Float): Bitmap? {
        if (!textureView.isAvailable) {
            return null
        }
        val width = (textureView.width * scale).toInt().coerceAtLeast(1)
        val height = (textureView.height * scale).toInt().coerceAtLeast(1)
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        return try {
            textureView.getBitmap(bitmap)
        } catch (_: Exception) {
            bitmap.recycle()
            null
        }
    }

    private fun captureTargets(activity: Activity): List<WindowCaptureTarget> {
        val activityRoot = activity.window.decorView.rootView
        val seen = linkedSetOf<Int>()
        val targets = mutableListOf<WindowCaptureTarget>()

        fun add(view: View, isActivityWindow: Boolean, index: Int) {
            if (!view.isRenderable()) {
                return
            }
            val key = System.identityHashCode(view.rootView ?: view)
            if (!seen.add(key)) {
                if (isActivityWindow) {
                    val existing = targets.indexOfFirst { System.identityHashCode(it.view.rootView ?: it.view) == key }
                    if (existing >= 0) {
                        targets[existing] = targets[existing].copy(isActivityWindow = true)
                    }
                }
                return
            }
            targets += WindowCaptureTarget(
                view = view.rootView ?: view,
                boundsInScreen = (view.rootView ?: view).boundsInScreen(),
                isActivityWindow = isActivityWindow,
                zIndex = index,
            )
        }

        reflectedWindowRoots(activity).forEachIndexed { index, view ->
            add(view, view.rootView === activityRoot || view === activityRoot, index)
        }
        add(activityRoot, isActivityWindow = true, index = Int.MIN_VALUE)

        return targets
            .filter { it.boundsInScreen.width() > 0 && it.boundsInScreen.height() > 0 }
            .sortedWith(compareBy<WindowCaptureTarget> { it.zIndex }.thenBy { if (it.isActivityWindow) 0 else 1 })
    }

    private fun reflectedWindowRoots(activity: Activity): List<View> {
        val activityRoot = activity.window.decorView.rootView
        return reflectedWindowRoots()
            .filter { view ->
                view.rootView === activityRoot ||
                    runCatching { view.context.applicationContext === activity.applicationContext }.getOrDefault(false)
            }
    }

    private fun reflectedWindowRoots(): List<View> {
        return try {
            val globalClass = Class.forName("android.view.WindowManagerGlobal")
            val getInstance = globalClass.getDeclaredMethod("getInstance")
            val global = getInstance.invoke(null)
            val viewsField = globalClass.getDeclaredField("mViews").apply { isAccessible = true }
            val views = viewsField.get(global)
            val rawViews = when (views) {
                is List<*> -> views
                is Array<*> -> views.toList()
                else -> emptyList<Any?>()
            }
            rawViews
                .filterIsInstance<View>()
                .map { it.rootView ?: it }
                .filter { view -> view.isRenderable() }
        } catch (_: Exception) {
            emptyList()
        }
    }

    fun visualTree(maxDepth: Int = 40, maxNodes: Int = 2_000): JSONObject {
        return runOnMain {
            val activity = currentActivity.get() ?: bindCurrentActivity() ?: error("No resumed Android activity is available for visual tree capture.")
            val root = activity.window.decorView.rootView ?: error("No Android root view is available for visual tree capture.")
            val counter = NodeCounter(maxNodes.coerceAtLeast(1))
            val typeRegistry = VisualTreeTypeRegistry()
            val rootNode = serializeView(root, "0", 0, maxDepth.coerceAtLeast(1), counter, typeRegistry)
            JSONObject()
                .put("platform", "android")
                .put("source", "native")
                .put("format", "ansight.native.visual-tree.compact.v2")
                .put("adapter", "android.views")
                .put("capturedAtUtc", AnsightClock.isoNow())
                .put("activity", activity.javaClass.name)
                .put("types", typeRegistry.toJson())
                .put("root", rootNode)
                .put("truncated", counter.truncated)
                .put("nodeCount", counter.count)
        }
    }

    fun inspectNode(nodeId: String): JSONObject {
        val tree = visualTree()
        val root = tree.optJSONObject("root") ?: error("Visual tree did not contain a root node.")
        val node = findNode(root, nodeId.trim()) ?: error("Node '$nodeId' was not found.")
        node.remove("children")
        return JSONObject()
            .put("format", tree.getString("format"))
            .put("platform", tree.getString("platform"))
            .put("source", tree.getString("source"))
            .put("adapter", tree.getString("adapter"))
            .put("capturedAtUtc", tree.getString("capturedAtUtc"))
            .put("types", tree.getJSONArray("types"))
            .put("node", node)
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
            .put("overlays", JSONArray(overlays.values.map { overlay -> overlayJson(overlay) }))
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
        if (existing != null && existing.activity.get() === activity && window.callback === existing) {
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

    private fun bindActivity(activity: Activity) {
        currentActivity = WeakReference(activity)
        installTouchCallback(activity)
        attachOverlaySurface(activity)
    }

    private fun reflectedWindowActivity(application: Application?): Activity? {
        return reflectedWindowRoots()
            .asSequence()
            .mapNotNull { view -> view.context.activityOrNull() }
            .firstOrNull { activity -> activity.isUsableFor(application) }
    }

    private fun reflectedActivityThreadActivity(application: Application?): Activity? {
        return try {
            val threadClass = Class.forName("android.app.ActivityThread")
            val currentThread = threadClass.getDeclaredMethod("currentActivityThread").invoke(null) ?: return null
            val activitiesField = threadClass.getDeclaredField("mActivities").apply { isAccessible = true }
            val records = (activitiesField.get(currentThread) as? Map<*, *>)?.values ?: return null
            val candidates = records.mapNotNull { record ->
                val actualRecord = record ?: return@mapNotNull null
                val activity = actualRecord.fieldValue("activity") as? Activity ?: return@mapNotNull null
                val paused = actualRecord.booleanFieldValue("paused") ?: actualRecord.booleanFieldValue("mPaused")
                val stopped = actualRecord.booleanFieldValue("stopped") ?: actualRecord.booleanFieldValue("mStopped")
                ActivityRecordCandidate(activity, paused, stopped)
            }
            candidates.firstOrNull { candidate ->
                candidate.activity.isUsableFor(application) && candidate.paused == false && candidate.stopped == false
            }?.activity ?: candidates.firstOrNull { candidate ->
                candidate.activity.isUsableFor(application)
            }?.activity
        } catch (_: Exception) {
            null
        }
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

    private fun serializeView(
        view: View,
        nodeId: String,
        depth: Int,
        maxDepth: Int,
        counter: NodeCounter,
        typeRegistry: VisualTreeTypeRegistry,
    ): JSONObject {
        if (!counter.tryEnter()) {
            val truncatedNode = JSONObject()
                .put("id", nodeId)
                .put("typeId", typeRegistry.typeId(view.javaClass.name))
                .put("truncated", true)
            zIndexForView(view)?.let { truncatedNode.put("z", it) }
            return truncatedNode
        }

        val location = IntArray(2)
        runCatching { view.getLocationOnScreen(location) }
        val resourceName = view.resourceNameOrNull()
        val automationId = resourceName ?: (view.tag as? String)?.trim()?.takeIf { it.isNotEmpty() }
        val actions = supportedActions(view)
        val json = JSONObject()
            .put("id", nodeId)
            .put("typeId", typeRegistry.typeId(view.javaClass.name))
            .put("automationId", automationId)
            .put("label", visualText(view) ?: view.contentDescription?.toString())
            .put("role", semanticRole(view))
            .put("supportedActions", JSONArray(actions))
            .put("interactable", view.visibility == View.VISIBLE && view.isEnabled && actions.isNotEmpty())
            .put("visible", view.visibility == View.VISIBLE)
            .put("enabled", view.isEnabled)
            .put("focused", view.isFocused)
            .put("bounds", JSONObject()
                .put("x", location[0])
                .put("y", location[1])
                .put("width", view.width)
            .put("height", view.height))
            .put("visual", visualForView(view))
            .put("importantForAccessibility", view.importantForAccessibility)
        zIndexForView(view)?.let { json.put("z", it) }

        if (view is ViewGroup && depth < maxDepth) {
            val children = JSONArray()
            for (index in 0 until view.childCount) {
                children.put(serializeView(view.getChildAt(index), "$nodeId.$index", depth + 1, maxDepth, counter, typeRegistry))
            }
            json.put("children", children)
        } else {
            json.put("children", JSONArray())
        }
        return json
    }

    private fun zIndexForView(view: View): Double? {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.LOLLIPOP || view.z == 0f) {
            return null
        }

        return view.z.toDouble()
    }

    private fun visualForView(view: View): JSONObject {
        val visual = JSONObject()
            .put("opacity", view.alpha.toDouble().coerceIn(0.0, 1.0))
        if (view is TextView) {
            visual.put("foreground", view.currentTextColor.toArgbHex())
        }
        view.backgroundColorOrNull()?.let { visual.put("background", it.toArgbHex()) }
        visual.putVisualString("text", visualText(view))

        when (view) {
            is EditText -> if (view.transformationMethod !is PasswordTransformationMethod) {
                visual.putVisualString("value", view.text?.toString())
            }
            is CompoundButton -> visual.put("value", view.isChecked.toString())
            is SeekBar -> visual.put("value", view.progress.toString())
        }

        return visual
    }

    private fun visualText(view: View): String? = when (view) {
        is EditText -> view.hint?.toString()
        is TextView -> view.text?.toString()
        else -> null
    }

    private fun semanticRole(view: View): String = when (view) {
        is Button -> "button"
        is EditText -> "textbox"
        is Switch -> "switch"
        is CheckBox -> "checkbox"
        is RadioButton -> "radio"
        is SeekBar -> "slider"
        is TextView -> "text"
        is ScrollView, is HorizontalScrollView -> "scrollview"
        is ViewGroup -> "group"
        else -> if (view.isClickable) "button" else "view"
    }

    private fun supportedActions(view: View): List<String> = buildList {
        if (view.isClickable || view is Button || view is CompoundButton) {
            add("tap")
        }
        if (view is EditText) {
            add("typeText")
            add("focus")
        }
        if (view is ScrollView || view is HorizontalScrollView) {
            add("scroll")
            add("swipe")
        }
    }

    private fun View.backgroundColorOrNull(): Int? {
        (background as? ColorDrawable)?.let { return it.color }
        return backgroundTintList?.getColorForState(drawableState, Color.TRANSPARENT)
    }

    private fun JSONObject.putVisualString(name: String, value: String?) {
        val normalized = value?.trim()?.takeIf { it.isNotEmpty() } ?: return
        put(name, if (normalized.length <= 240) normalized else normalized.take(240) + "...")
    }

    private fun Int.toArgbHex(): String = String.format(Locale.US, "#%08X", this)

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

    private data class WindowCaptureTarget(
        val view: View,
        val boundsInScreen: Rect,
        val isActivityWindow: Boolean,
        val zIndex: Int,
    )

    private data class ActivityRecordCandidate(
        val activity: Activity,
        val paused: Boolean?,
        val stopped: Boolean?,
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

    private class VisualTreeTypeRegistry {
        private val idsByTypeName = linkedMapOf<String, Int>()

        fun typeId(typeName: String): Int {
            val normalizedTypeName = typeName.trim().ifEmpty { "UnknownView" }
            return idsByTypeName.getOrPut(normalizedTypeName) { idsByTypeName.size }
        }

        fun toJson(): JSONArray {
            return JSONArray().apply {
                idsByTypeName.keys.forEach(::put)
            }
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

private fun View.boundsInScreen(): Rect {
    val location = IntArray(2)
    runCatching { getLocationOnScreen(location) }
    return Rect(location[0], location[1], location[0] + width, location[1] + height)
}

private fun View.isRenderable(): Boolean =
    width > 0 && height > 0 && visibility == View.VISIBLE && isShown && alpha > 0f

private fun Activity.isUsableFor(application: Application?): Boolean {
    if (application != null && this.application !== application) {
        return false
    }
    if (isFinishing) {
        return false
    }
    if (booleanFieldValue("mStopped") == true) {
        return false
    }
    return Build.VERSION.SDK_INT < Build.VERSION_CODES.JELLY_BEAN_MR1 || !isDestroyed
}

private fun Context.activityOrNull(): Activity? {
    var current: Context? = this
    while (current is ContextWrapper) {
        if (current is Activity) {
            return current
        }
        current = current.baseContext
    }
    return current as? Activity
}

private fun Any.fieldValue(name: String): Any? {
    var type: Class<*>? = javaClass
    while (type != null) {
        try {
            return type.getDeclaredField(name).apply { isAccessible = true }.get(this)
        } catch (_: Exception) {
            type = type.superclass
        }
    }
    return null
}

private fun Any.booleanFieldValue(name: String): Boolean? = fieldValue(name) as? Boolean

private fun View.forEachDescendant(action: (View) -> Unit) {
    action(this)
    if (this is ViewGroup) {
        for (index in 0 until childCount) {
            getChildAt(index).forEachDescendant(action)
        }
    }
}

private fun String.toColorOrNull(): Int? = try {
    Color.parseColor(this)
} catch (_: Exception) {
    null
}
