package ai.ansight.runtime

import android.app.Activity
import android.app.Application
import android.app.Dialog
import android.content.pm.ApplicationInfo
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.os.Handler
import android.os.Looper
import android.view.MotionEvent
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageButton
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.ByteArrayInputStream
import java.io.File
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean
import java.util.zip.ZipEntry
import java.util.zip.ZipInputStream
import java.util.zip.ZipOutputStream
import kotlin.coroutines.resume
import kotlin.coroutines.suspendCoroutine

/** Native annotation capture and delivery for the Android core package. */
object Annotate {
    private val mainHandler by lazy { Handler(Looper.getMainLooper()) }
    private val gate = Any()
    private var application: Application? = null
    private var options = AnnotationOptions()
    private var presenting = false
    private var connectionSubscription: HostConnectionStatusSubscription? = null
    private val flushing = AtomicBoolean(false)

    internal fun initialize(application: Application, options: AnnotationOptions) {
        connectionSubscription?.remove()
        connectionSubscription = null
        synchronized(gate) {
            this.application = application
            this.options = options.validated()
        }
    }

    internal fun attachToRuntime() {
        connectionSubscription = AnsightRuntime.addHostConnectionStatusListener({ status, _ ->
            if (status.isConnected) flushOutbox()
        })
    }

    @JvmStatic
    val isEnabled: Boolean
        get() = synchronized(gate) {
            val app = application ?: return@synchronized false
            options.enabled && app.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE != 0
        }

    /** Captures evidence before opening the native editor. The callback is invoked on the main thread. */
    @JvmStatic
    @JvmOverloads
    fun PresentAsync(activity: Activity? = null, completion: (AnnotationCaptureResult) -> Unit) {
        val app = synchronized(gate) { application }
        if (app == null || !isEnabled) {
            complete(completion, AnnotationCaptureResult(AnnotationCaptureStatus.Disabled, message = "Annotations require an initialized Debug application."))
            return
        }
        synchronized(gate) {
            if (presenting) {
                complete(completion, AnnotationCaptureResult(AnnotationCaptureStatus.Unavailable, message = "An annotation editor is already open."))
                return
            }
            presenting = true
        }
        val captureOptions = synchronized(gate) { options }
        val annotationId = UUID.randomUUID().toString()
        val captureGroupId = UUID.randomUUID().toString()
        val capturedAtUtc = AnsightClock.isoNow()
        Thread {
            try {
                val currentActivity = activity ?: AndroidUiEvidence.bindCurrentActivity(app)
                if (currentActivity == null) {
                    finish(completion, AnnotationCaptureResult(AnnotationCaptureStatus.Unavailable, message = "No foreground Android activity is available."))
                    return@Thread
                }
                val screenshot = if (captureOptions.captureScreenshot) {
                    runCatching {
                        AndroidUiEvidence.captureScreenshot("jpeg", captureOptions.screenshotQuality, captureOptions.screenshotMaxWidth)
                    }.getOrNull()
                } else null
                val screenshotAtUtc = AnsightClock.isoNow()
                val visualTree = if (captureOptions.captureVisualTrees) {
                    runCatching {
                        AnsightRuntime.captureAnnotationVisualTrees()
                    }.getOrDefault(emptyList())
                } else emptyList()
                val treeAtUtc = AnsightClock.isoNow()
                mainHandler.post {
                    if (currentActivity.isFinishing) {
                        finish(completion, AnnotationCaptureResult(AnnotationCaptureStatus.Unavailable, message = "The foreground activity has closed."))
                        return@post
                    }
                    presentEditor(currentActivity, screenshot) { feedback, shapes ->
                        if (feedback == null) {
                            finish(completion, AnnotationCaptureResult(AnnotationCaptureStatus.Cancelled))
                        } else {
                            Thread {
                                val result = runCatching {
                                    val bundle = makeBundle(
                                        annotationId, captureGroupId, capturedAtUtc,
                                        screenshotAtUtc, screenshot, treeAtUtc, visualTree,
                                        captureOptions, feedback, shapes,
                                    )
                                    storeAndSubmit(app, annotationId, capturedAtUtc, bundle)
                                }.getOrElse { error ->
                                    AnnotationCaptureResult(AnnotationCaptureStatus.Failed, annotationId, error.message)
                                }
                                finish(completion, result)
                            }.apply { name = "AnsightAnnotationDelivery"; start() }
                        }
                    }
                }
            } catch (error: Exception) {
                finish(completion, AnnotationCaptureResult(AnnotationCaptureStatus.Failed, annotationId, error.message))
            }
        }.apply { name = "AnsightAnnotationCapture"; start() }
    }

    suspend fun PresentAsync(activity: Activity? = null): AnnotationCaptureResult = suspendCoroutine { continuation ->
        PresentAsync(activity) { continuation.resume(it) }
    }

    private fun presentEditor(
        activity: Activity,
        screenshot: CapturedScreenshot?,
        completion: (String?, JSONArray) -> Unit,
    ) {
        val dialog = Dialog(activity)
        val column = LinearLayout(activity).apply {
            orientation = LinearLayout.VERTICAL
            isFocusableInTouchMode = true
            setBackgroundColor(Color.BLACK)
        }
        val density = activity.resources.displayMetrics.density
        fun dp(value: Int) = (value * density).toInt()
        val header = LinearLayout(activity).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = android.view.Gravity.CENTER_VERTICAL
            setPadding(dp(16), dp(4), dp(16), dp(4))
        }
        header.addView(ImageView(activity).apply {
            setImageResource(R.drawable.ansight_annotation_icon)
            scaleType = ImageView.ScaleType.FIT_CENTER
            contentDescription = "Ansight logo"
        }, LinearLayout.LayoutParams(dp(36), dp(36)))
        header.addView(TextView(activity).apply {
            text = "Ansight Annotation"
            textSize = 17f
            setTextColor(Color.WHITE)
            setTypeface(typeface, android.graphics.Typeface.BOLD)
        }, LinearLayout.LayoutParams(-2, dp(36)))
        column.addView(header)
        val drawing = AnnotationDrawingView(activity)
        val image = ImageView(activity).apply {
            scaleType = ImageView.ScaleType.FIT_XY
            screenshot?.bytes?.let { bytes ->
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.let { setImageBitmap(it) }
            }
        }
        val frame = FrameLayout(activity).apply {
            addView(image, FrameLayout.LayoutParams(-1, -1))
            addView(drawing, FrameLayout.LayoutParams(-1, -1))
        }
        column.addView(frame, LinearLayout.LayoutParams(-1, 0, 1f))
        val feedback = EditText(activity).apply {
            hint = "Describe the issue"
            setTextColor(Color.WHITE)
            setHintTextColor(Color.LTGRAY)
            minLines = 2
            maxLines = 4
            imeOptions = EditorInfo.IME_ACTION_DONE
        }
        feedback.setOnFocusChangeListener { _, hasFocus ->
            header.visibility = if (hasFocus) View.GONE else View.VISIBLE
        }
        fun dismissKeyboard() {
            (activity.getSystemService(Activity.INPUT_METHOD_SERVICE) as? InputMethodManager)
                ?.hideSoftInputFromWindow(feedback.windowToken, 0)
            feedback.clearFocus()
        }
        feedback.setOnEditorActionListener { _, actionId, _ ->
            if (actionId == EditorInfo.IME_ACTION_DONE) { dismissKeyboard(); true } else false
        }
        val inputRow = LinearLayout(activity).apply { orientation = LinearLayout.HORIZONTAL }
        inputRow.addView(feedback, LinearLayout.LayoutParams(0, -2, 1f))
        inputRow.addView(Button(activity).apply {
            text = "Done"
            contentDescription = "Dismiss keyboard"
            setOnClickListener { dismissKeyboard() }
        }, LinearLayout.LayoutParams(dp(72), dp(48)))
        column.addView(inputRow, LinearLayout.LayoutParams(-1, -2))
        val buttons = LinearLayout(activity)
        fun button(label: String, icon: Int, action: () -> Unit) {
            buttons.addView(ImageButton(activity).apply {
                setImageResource(icon)
                contentDescription = label
                setOnClickListener { action() }
            }, LinearLayout.LayoutParams(0, dp(50), 1f))
        }
        var completed = false
        fun completeOnce(text: String?) {
            if (completed) return
            completed = true
            dialog.dismiss()
            completion(text, drawing.shapes())
        }
        button("Cancel", android.R.drawable.ic_menu_close_clear_cancel) { completeOnce(null) }
        button("Undo", android.R.drawable.ic_menu_revert) { drawing.undo() }
        button("Clear", android.R.drawable.ic_menu_delete) { drawing.clear() }
        button("Save", android.R.drawable.ic_menu_save) { completeOnce(feedback.text.toString()) }
        column.addView(buttons, LinearLayout.LayoutParams(-1, -2))
        dialog.setContentView(column)
        dialog.window?.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        dialog.setOnCancelListener { completeOnce(null) }
        dialog.show()
        dialog.window?.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT)
        dialog.window?.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE or WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN)
        column.requestFocus()
    }

    private fun makeBundle(
        annotationId: String,
        captureGroupId: String,
        capturedAtUtc: String,
        screenshotAtUtc: String,
        screenshot: CapturedScreenshot?,
        treeAtUtc: String,
        visualTree: List<JSONObject>,
        captureOptions: AnnotationOptions,
        feedback: String,
        shapes: JSONArray,
    ): ByteArray {
        val output = ByteArrayOutputStream()
        ZipOutputStream(output).use { zip ->
            fun entry(path: String, bytes: ByteArray) {
                zip.putNextEntry(ZipEntry(path))
                zip.write(bytes)
                zip.closeEntry()
            }
            val manifest = JSONObject()
                .put("schema", "ansight.annotation.bundle.v1")
                .put("version", 1)
                .put("annotationId", annotationId)
                .put("captureGroupId", captureGroupId)
                .put("capturedAtUtc", capturedAtUtc)
                .put("feedback", feedback.trim())
                .put("shapes", shapes)
                .put("visualTrees", JSONArray())
                .put("artifacts", JSONArray())
                .put("evidence", JSONArray())
            val evidence = manifest.getJSONArray("evidence")
            if (screenshot != null) {
                val path = "evidence/screenshot.jpg"
                entry(path, screenshot.bytes)
                manifest.put("screenshot", JSONObject()
                    .put("path", path).put("mimeType", screenshot.mimeType)
                    .put("width", screenshot.width).put("height", screenshot.height)
                    .put("capturedAtUtc", screenshotAtUtc))
            }
            evidence.put(JSONObject()
                .put("id", "screenshot")
                .put("kind", "screenshot")
                .put("status", if (screenshot != null) "captured" else if (captureOptions.captureScreenshot) "unavailable" else "skipped")
                .put("capturedAtUtc", if (screenshot != null) screenshotAtUtc else JSONObject.NULL)
                .put("sizeBytes", screenshot?.bytes?.size ?: JSONObject.NULL))
            for ((index, tree) in visualTree.withIndex()) {
                val path = "evidence/visual-trees/tree-$index.json"
                entry(path, tree.toString().toByteArray(Charsets.UTF_8))
                val source = tree.optString("source", "native")
                manifest.getJSONArray("visualTrees").put(JSONObject()
                    .put("source", source).put("displayName", source)
                    .put("path", path).put("capturedAtUtc", treeAtUtc)
                    .put("truncated", tree.optBoolean("truncated")))
                evidence.put(JSONObject()
                    .put("id", "visual-tree:$source")
                    .put("kind", "visualTree")
                    .put("status", "captured")
                    .put("capturedAtUtc", treeAtUtc)
                    .put("sizeBytes", tree.toString().toByteArray(Charsets.UTF_8).size)
                    .put("truncated", tree.optBoolean("truncated")))
            }
            if (visualTree.isEmpty()) {
                evidence.put(JSONObject()
                    .put("id", "visual-tree")
                    .put("kind", "visualTree")
                    .put("status", if (captureOptions.captureVisualTrees) "unavailable" else "skipped"))
            }
            entry("manifest.json", manifest.toString().toByteArray(Charsets.UTF_8))
        }
        return output.toByteArray()
    }

    private fun storeAndSubmit(app: Application, annotationId: String, capturedAtUtc: String, bytes: ByteArray): AnnotationCaptureResult {
        val directory = File(app.filesDir, "ansight/annotations/outbox").apply { mkdirs() }
        val file = File(directory, "$annotationId.ansightannotation")
        val temporary = File(directory, "$annotationId.tmp")
        temporary.writeBytes(bytes)
        check(temporary.renameTo(file)) { "Could not persist the annotation bundle." }
        val result = submit(annotationId, capturedAtUtc, bytes)
        if (result.success) file.delete()
        return AnnotationCaptureResult(
            if (result.success) AnnotationCaptureStatus.Completed else AnnotationCaptureStatus.Queued,
            annotationId, result.message,
        )
    }

    private fun flushOutbox() {
        if (!flushing.compareAndSet(false, true)) return
        val app = synchronized(gate) { application }
        Thread {
            try {
                val directory = app?.let { File(it.filesDir, "ansight/annotations/outbox") } ?: return@Thread
                for (file in directory.listFiles().orEmpty().filter { it.extension == "ansightannotation" }.sortedBy { it.lastModified() }) {
                    val bytes = runCatching { file.readBytes() }.getOrNull() ?: continue
                    val capturedAtUtc = runCatching {
                        ZipInputStream(ByteArrayInputStream(bytes)).use { zip ->
                            while (true) {
                                val entry = zip.nextEntry ?: break
                                if (entry.name == "manifest.json") {
                                    return@use JSONObject(zip.readBytes().toString(Charsets.UTF_8)).getString("capturedAtUtc")
                                }
                            }
                            null
                        }
                    }.getOrNull() ?: continue
                    val annotationId = file.nameWithoutExtension
                    if (submit(annotationId, capturedAtUtc, bytes).success) file.delete() else break
                }
            } finally {
                flushing.set(false)
            }
        }.apply { name = "AnsightAnnotationOutbox"; isDaemon = true; start() }
    }

    private fun submit(annotationId: String, capturedAtUtc: String, bytes: ByteArray): OperationResult {
        val transferId = PairingFileTransferWireProtocol.newTransferId()
        val chunkBytes = 64 * 1024
        val payload = JSONObject()
            .put("schema", "ansight.annotation.submit.v1")
            .put("clientAnnotationId", annotationId.replace("-", ""))
            .put("capturedAtUtc", capturedAtUtc)
            .put("transfer", JSONObject()
                .put("transferId", transferId)
                .put("fileName", "$annotationId.ansightannotation")
                .put("mimeType", "application/vnd.ansight.annotation+zip")
                .put("sizeBytes", bytes.size)
                .put("chunkBytes", chunkBytes)
                .put("wireProtocol", PairingFileTransferWireProtocol.ProtocolName))
        val ready = AnsightRuntime.sendControlRequest("annotation.submit", payload)
        if (!ready.success) return ready
        var offset = 0
        var sequence = 0
        while (offset < bytes.size) {
            val end = (offset + chunkBytes).coerceAtMost(bytes.size)
            val frame = PairingFileTransferWireProtocol.createFrame(
                transferId, FileTransferFrameType.Chunk, sequence, offset.toLong(), bytes.copyOfRange(offset, end),
            )
            val sent = AnsightRuntime.sendBinaryData(frame)
            if (!sent.success) return sent
            offset = end
            sequence++
        }
        return AnsightRuntime.sendBinaryData(PairingFileTransferWireProtocol.createFrame(
            transferId, FileTransferFrameType.Complete, sequence, bytes.size.toLong(), ByteArray(0),
        ))
    }

    private fun complete(completion: (AnnotationCaptureResult) -> Unit, result: AnnotationCaptureResult) {
        mainHandler.post { completion(result) }
    }

    private fun finish(completion: (AnnotationCaptureResult) -> Unit, result: AnnotationCaptureResult) {
        synchronized(gate) { presenting = false }
        complete(completion, result)
    }
}

private class AnnotationDrawingView(activity: Activity) : View(activity) {
    private val paths = mutableListOf<MutableList<Pair<Float, Float>>>()
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.RED
        strokeWidth = 6f
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }

    override fun onDraw(canvas: Canvas) {
        super.onDraw(canvas)
        for (points in paths) {
            if (points.size < 2) continue
            val path = android.graphics.Path()
            path.moveTo(points[0].first * width, points[0].second * height)
            for (point in points.drop(1)) path.lineTo(point.first * width, point.second * height)
            canvas.drawPath(path, paint)
        }
    }

    override fun onTouchEvent(event: MotionEvent): Boolean {
        if (width <= 0 || height <= 0) return false
        val point = (event.x / width).coerceIn(0f, 1f) to (event.y / height).coerceIn(0f, 1f)
        when (event.actionMasked) {
            MotionEvent.ACTION_DOWN -> paths.add(mutableListOf(point))
            MotionEvent.ACTION_MOVE, MotionEvent.ACTION_UP -> paths.lastOrNull()?.add(point)
            else -> return false
        }
        invalidate()
        return true
    }

    fun undo() { if (paths.isNotEmpty()) { paths.removeAt(paths.lastIndex); invalidate() } }
    fun clear() { paths.clear(); invalidate() }

    fun shapes(): JSONArray = JSONArray().also { shapes ->
        for (path in paths.filter { it.size >= 2 }) {
            val x = path.minOf { it.first }.toDouble()
            val y = path.minOf { it.second }.toDouble()
            shapes.put(JSONObject()
                .put("kind", "freeDraw")
                .put("x", x).put("y", y)
                .put("width", path.maxOf { it.first } - x)
                .put("height", path.maxOf { it.second } - y)
                .put("points", JSONArray().also { points ->
                    path.forEach { (px, py) -> points.put(JSONObject().put("x", px).put("y", py)) }
                })
                .put("text", "")
                .put("strokeColor", "#FFFF3B30")
                .put("strokeWidth", 3))
        }
    }
}
