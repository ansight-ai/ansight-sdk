package ai.ansight.runtime

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Rect
import android.graphics.drawable.AdaptiveIconDrawable
import android.graphics.drawable.BitmapDrawable
import android.graphics.drawable.ColorDrawable
import android.util.Base64
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode

@RunWith(RobolectricTestRunner::class)
@Config(manifest = Config.NONE, sdk = [28])
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class DeviceApplicationIconCollectorTest {
    @Test
    fun collectedProfileIncludesDecodableAdaptiveIconInHostProtocolShape() {
        val application = RuntimeEnvironment.getApplication()
        val drawable = AdaptiveIconDrawable(ColorDrawable(Color.RED), ColorDrawable(Color.BLUE))
        val originalBounds = Rect(2, 3, 40, 50)
        drawable.bounds = originalBounds
        shadowOf(application.packageManager).setApplicationIcon(application.packageName, drawable)

        val app = DeviceAppProfileCollector.collect(application, profileSeq = 1).toJson().getJSONObject("app")
        val icon = app.getJSONObject("icon")
        val encoded = icon.getString("dataBase64")
        val bytes = Base64.decode(encoded, Base64.DEFAULT)
        val bitmap = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
        try {
            assertEquals("png", icon.getString("format"))
            assertEquals("image/png", icon.getString("mimeType"))
            assertEquals(bytes.size, icon.getInt("byteCount"))
            assertNotNull(bitmap)
            assertEquals(bitmap.width, icon.getInt("width"))
            assertEquals(bitmap.height, icon.getInt("height"))
            assertTrue(bitmap.width in 1..256)
            assertTrue(bitmap.height in 1..256)
            assertEquals(Color.BLUE, bitmap.getPixel(bitmap.width / 2, bitmap.height / 2))
            assertFalse(encoded.contains('\n'))
            assertFalse(icon.has("widthPx"))
            assertFalse(icon.has("heightPx"))
            assertEquals(originalBounds, drawable.bounds)
        } finally {
            bitmap?.recycle()
        }
    }

    @Test
    fun largeBitmapIsDownscaledWithoutChangingAspectRatio() {
        val application = RuntimeEnvironment.getApplication()
        val bitmap = Bitmap.createBitmap(1024, 512, Bitmap.Config.ARGB_8888)
        try {
            bitmap.eraseColor(Color.GREEN)
            val drawable = BitmapDrawable(application.resources, bitmap)
            drawable.setTargetDensity(bitmap.density)
            val icon = DeviceApplicationIconCollector().render(drawable)!!
            assertEquals(256, icon.width)
            assertEquals(128, icon.height)
            assertFalse(bitmap.isRecycled)
        } finally {
            bitmap.recycle()
        }
    }

    @Test
    fun drawableWithoutIntrinsicSizeIsBoundedAndCachedForRepeatedProfiles() {
        val application = RuntimeEnvironment.getApplication()
        var drawCount = 0
        val drawable = object : ColorDrawable(Color.RED) {
            override fun draw(canvas: Canvas) {
                drawCount++
                super.draw(canvas)
            }
        }
        shadowOf(application.packageManager).setApplicationIcon(application.packageName, drawable)
        val collector = DeviceApplicationIconCollector()

        val icon = collector.collect(application)!!
        assertEquals(256, icon.width)
        assertEquals(256, icon.height)
        assertSame(icon, collector.collect(application))
        assertEquals(1, drawCount)
    }

    @Test
    fun brokenIconIsOptionalAndNotRetriedOnEverySample() {
        val application = RuntimeEnvironment.getApplication()
        var drawCount = 0
        val drawable = object : ColorDrawable(Color.RED) {
            override fun draw(canvas: Canvas) {
                drawCount++
                error("Unreadable icon")
            }
        }
        val originalBounds = Rect(1, 2, 30, 40)
        drawable.bounds = originalBounds
        shadowOf(application.packageManager).setApplicationIcon(application.packageName, drawable)
        val collector = DeviceApplicationIconCollector()

        assertNull(collector.collect(application))
        assertNull(collector.collect(application))
        assertEquals(1, drawCount)
        assertEquals(originalBounds, drawable.bounds)
        val profile = DeviceAppProfileCollector.collect(application, profileSeq = 1)
        assertEquals(application.packageName, profile.app.appId)
        assertFalse(profile.toJson().getJSONObject("app").has("icon"))
    }
}
