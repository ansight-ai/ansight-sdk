package ai.ansight.runtime

import android.os.Build
import android.view.View
import android.view.WindowInsets
import org.json.JSONArray
import org.json.JSONObject

/** Geometry belongs to the final image, with every coordinate normalized to 0..1. */
data class ScreenshotCaptureGeometry(
    val captureTarget: String,
    val safeAreaInsets: ScreenshotInsets?,
    val systemUi: Map<String, ScreenshotSystemUiRegion>,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("coordinateSpace", "image-normalized")
        .put("captureTarget", captureTarget)
        .put("safeAreaInsets", safeAreaInsets?.toJson() ?: JSONObject.NULL)
        .put("systemUi", JSONObject().apply { systemUi.forEach { (kind, region) -> put(kind, region.toJson()) } })

    companion object {
        /** App-window PixelCopy/drawing excludes the separately composed OS bar glyphs. */
        internal fun appWindow(root: View): ScreenshotCaptureGeometry {
            val width = root.width.toDouble()
            val height = root.height.toDouble()
            val insets = if (Build.VERSION.SDK_INT >= 23) root.rootWindowInsets else null
            val safeArea = if (width > 0 && height > 0 && insets != null) {
                if (Build.VERSION.SDK_INT >= 30) {
                    val safe = insets.getInsets(WindowInsets.Type.systemBars() or WindowInsets.Type.displayCutout())
                    ScreenshotInsets(safe.top / height, safe.right / width, safe.bottom / height, safe.left / width)
                } else {
                    // Stable insets do not include the keyboard. Older APIs cannot report transient bar bounds.
                    @Suppress("DEPRECATION")
                    ScreenshotInsets(insets.stableInsetTop / height, insets.stableInsetRight / width,
                        insets.stableInsetBottom / height, insets.stableInsetLeft / width)
                }.takeIf { it.isValid() }
            } else null
            val cutout = if (Build.VERSION.SDK_INT >= 28 && insets != null && width > 0 && height > 0) {
                val regions = insets.displayCutout?.boundingRects.orEmpty().mapNotNull { rect ->
                    val left = rect.left.coerceIn(0, root.width)
                    val top = rect.top.coerceIn(0, root.height)
                    val right = rect.right.coerceIn(0, root.width)
                    val bottom = rect.bottom.coerceIn(0, root.height)
                    if (right <= left || bottom <= top) null else
                        ScreenshotRect(left / width, top / height, (right - left) / width, (bottom - top) / height)
                }
                if (regions.isEmpty()) ScreenshotSystemUiRegion.absent("No display cutout intersects this window.")
                else ScreenshotSystemUiRegion("present", regions)
            } else ScreenshotSystemUiRegion.unknown("Display cutout bounds are unavailable on this capture.")
            return ScreenshotCaptureGeometry("appWindow", safeArea, mapOf(
                "statusBar" to ScreenshotSystemUiRegion.absent("App-window capture excludes the OS status-bar layer."),
                "navigationBar" to ScreenshotSystemUiRegion.absent("App-window capture excludes the OS navigation-bar layer."),
                "displayCutout" to cutout,
            ))
        }
    }
}

data class ScreenshotInsets(val top: Double, val right: Double, val bottom: Double, val left: Double) {
    fun isValid(): Boolean = listOf(top, right, bottom, left).all { it.isFinite() && it >= 0 && it <= 1 }
        && top + bottom < 1 && left + right < 1
    fun toJson(): JSONObject = JSONObject().put("top", top).put("right", right).put("bottom", bottom).put("left", left)
}

data class ScreenshotRect(val x: Double, val y: Double, val width: Double, val height: Double) {
    fun toJson(): JSONObject = JSONObject().put("x", x).put("y", y).put("width", width).put("height", height)
}

data class ScreenshotSystemUiRegion(val state: String, val regions: List<ScreenshotRect> = emptyList(), val reason: String? = null) {
    fun toJson(): JSONObject = JSONObject().put("state", state)
        .put("regions", JSONArray().apply { regions.forEach { put(it.toJson()) } })
        .apply { reason?.let { put("reason", it) } }
    companion object {
        fun absent(reason: String) = ScreenshotSystemUiRegion("absent", reason = reason)
        fun unknown(reason: String) = ScreenshotSystemUiRegion("unknown", reason = reason)
    }
}
