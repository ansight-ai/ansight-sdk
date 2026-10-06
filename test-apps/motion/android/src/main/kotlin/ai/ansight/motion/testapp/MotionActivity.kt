package ai.ansight.motion.testapp

import ai.ansight.Ansight
import ai.ansight.motion.AnsightMotion
import ai.ansight.runtime.AnsightOptionsBuilder
import android.app.Activity
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Bundle
import android.os.SystemClock
import android.view.Gravity
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import java.util.Locale
import kotlin.math.abs

/** A deliberately small app that owns its accelerometer subscription. */
class MotionActivity : Activity(), SensorEventListener {
    private lateinit var sensorManager: SensorManager
    private lateinit var accelerometer: Sensor
    private lateinit var status: TextView
    private lateinit var sampleCountView: TextView
    private lateinit var shakeCountView: TextView
    private lateinit var shakeStatus: TextView
    private lateinit var customStatus: TextView
    private var listenerEnabled = false
    private var sampleCount = 0
    private var shakeCount = 0
    private var lastX = 0f
    private var lastShakeAtMs = 0L
    private var customSampleObserved = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        sensorManager = getSystemService(SENSOR_SERVICE) as SensorManager
        val sensor = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
        if (sensor != null) accelerometer = sensor

        val options = AnsightOptionsBuilder(
            Ansight.developerOptions(clientName = "Motion Android Test")
        ).withMotionCapture().withUnattendedProvisioning().build()
        runCatching { Ansight.initializeAndActivate(application, options) }

        val layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER_HORIZONTAL
            setPadding(32, 48, 32, 32)
        }
        layout.addView(TextView(this).apply {
            text = "Motion capture test"
            textSize = 26f
        })
        status = TextView(this).apply {
            contentDescription = "motion.status"
            textSize = 18f
            setPadding(0, 30, 0, 30)
        }
        layout.addView(status)
        sampleCountView = TextView(this).apply {
            contentDescription = "motion.samples"
            textSize = 18f
        }
        layout.addView(sampleCountView)
        shakeCountView = TextView(this).apply {
            contentDescription = "motion.shakes"
            textSize = 18f
        }
        layout.addView(shakeCountView)
        shakeStatus = TextView(this).apply {
            contentDescription = "motion.shake-status"
            textSize = 18f
        }
        layout.addView(shakeStatus)
        customStatus = TextView(this).apply {
            contentDescription = "motion.custom-sample-status"
            textSize = 18f
        }
        layout.addView(customStatus)
        layout.addView(actionButton("Enable accelerometer", "motion.enable") { setListenerEnabled(true) })
        layout.addView(actionButton("Disable accelerometer", "motion.disable") { setListenerEnabled(false) })
        layout.addView(actionButton("Reset counters", "motion.reset") {
            sampleCount = 0
            shakeCount = 0
            customSampleObserved = false
            lastX = 0f
            lastShakeAtMs = 0L
            renderStatus()
        })
        layout.addView(actionButton("Record shake manually", "motion.manual-shake") {
            AnsightMotion.recordShake("test-button")
            shakeCount++
            renderStatus()
        })
        setContentView(layout, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        renderStatus()
    }

    override fun onResume() {
        super.onResume()
        if (listenerEnabled) registerListener()
    }

    override fun onPause() {
        sensorManager.unregisterListener(this)
        super.onPause()
    }

    override fun onDestroy() {
        sensorManager.unregisterListener(this)
        super.onDestroy()
    }

    private fun setListenerEnabled(enabled: Boolean) {
        listenerEnabled = enabled && ::accelerometer.isInitialized
        sensorManager.unregisterListener(this)
        if (listenerEnabled) registerListener()
        renderStatus()
    }

    private fun registerListener() {
        if (!sensorManager.registerListener(this, accelerometer, SensorManager.SENSOR_DELAY_GAME)) {
            listenerEnabled = false
        }
    }

    override fun onSensorChanged(event: SensorEvent) {
        if (!listenerEnabled || event.sensor.type != Sensor.TYPE_ACCELEROMETER) return
        val x = event.values[0]
        val y = event.values[1]
        val z = event.values[2]
        AnsightMotion.recordAccelerometer(x.toDouble(), y.toDouble(), z.toDouble())
        sampleCount++
        if (x >= 30f) customSampleObserved = true
        val now = SystemClock.elapsedRealtime()
        if (abs(x - lastX) >= 25f && now - lastShakeAtMs >= 600) {
            AnsightMotion.recordShake("accelerometer-detector")
            shakeCount++
            lastShakeAtMs = now
        }
        lastX = x
        renderStatus()
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) = Unit

    private fun actionButton(label: String, automationId: String, action: () -> Unit): Button = Button(this).apply {
        text = label
        contentDescription = automationId
        setOnClickListener { action() }
    }

    private fun renderStatus() {
        val state = if (listenerEnabled) "enabled" else "disabled"
        status.text = "Listener: $state"
        sampleCountView.text = String.format(Locale.US, "Samples: %d", sampleCount)
        shakeCountView.text = String.format(Locale.US, "Shakes: %d", shakeCount)
        shakeStatus.text = if (shakeCount > 0) "Shake detected" else "Shake waiting"
        customStatus.text = if (customSampleObserved) "Custom sample observed" else "Custom sample waiting"
    }
}
