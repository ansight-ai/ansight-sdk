package ai.ansight.runtime

import android.app.Application
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Rect
import android.graphics.drawable.Drawable
import android.util.Base64
import java.io.ByteArrayOutputStream
import kotlin.math.roundToInt

internal class DeviceApplicationIconCollector {
    private var cachedApplication: Application? = null
    private var cachedIcon: DeviceApplicationIconProfile? = null

    @Synchronized
    fun collect(application: Application): DeviceApplicationIconProfile? {
        if (cachedApplication === application) return cachedIcon

        // Profiles are also collected during battery sampling. Render at most once per
        // app process, including failures, so optional metadata never adds recurring work.
        cachedIcon = runCatching {
            render(application.packageManager.getApplicationIcon(application.packageName))
        }.getOrNull()
        cachedApplication = application
        return cachedIcon
    }

    internal fun render(drawable: Drawable): DeviceApplicationIconProfile? {
        val sourceWidth = drawable.intrinsicWidth.takeIf { it > 0 } ?: maximumPixelLength
        val sourceHeight = drawable.intrinsicHeight.takeIf { it > 0 } ?: maximumPixelLength
        val scale = minOf(1.0, maximumPixelLength.toDouble() / maxOf(sourceWidth, sourceHeight))
        val width = (sourceWidth * scale).roundToInt().coerceAtLeast(1)
        val height = (sourceHeight * scale).roundToInt().coerceAtLeast(1)
        val originalBounds = Rect(drawable.bounds)
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        try {
            // Drawing the Drawable supports adaptive, vector and bitmap launcher icons.
            drawable.setBounds(0, 0, width, height)
            drawable.draw(Canvas(bitmap))
            val output = ByteArrayOutputStream()
            if (!bitmap.compress(Bitmap.CompressFormat.PNG, 100, output)) return null
            val bytes = output.toByteArray()
            if (bytes.isEmpty() || bytes.size > maximumByteCount) return null
            return DeviceApplicationIconProfile(
                format = "png",
                mimeType = "image/png",
                width = width,
                height = height,
                byteCount = bytes.size,
                dataBase64 = Base64.encodeToString(bytes, Base64.NO_WRAP),
            )
        } finally {
            drawable.bounds = originalBounds
            bitmap.recycle()
        }
    }

    private companion object {
        const val maximumPixelLength = 256
        const val maximumByteCount = 2 * 1024 * 1024
    }
}
