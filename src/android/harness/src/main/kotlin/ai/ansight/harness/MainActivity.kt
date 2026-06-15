package ai.ansight.harness

import android.os.Bundle
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import ai.ansight.runtime.AnsightEventType
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime

class MainActivity : AppCompatActivity() {
    private lateinit var snapshotView: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        snapshotView = findViewById(R.id.snapshotView)
        runCatching {
            AnsightRuntime.initializeAndActivate(
                application = application,
                options = AnsightOptions(enableFramesPerSecond = true, enableBatteryLevel = true),
            )
            AnsightRuntime.screenViewed("Harness")
        }

        findViewById<Button>(R.id.initializeButton).setOnClickListener {
            AnsightRuntime.initialize(
                application = application,
                options = AnsightOptions(enableFramesPerSecond = true),
            )
            renderSnapshot()
        }

        findViewById<Button>(R.id.activateButton).setOnClickListener {
            runCatching { AnsightRuntime.activate() }
            renderSnapshot()
        }

        findViewById<Button>(R.id.metricButton).setOnClickListener {
            runCatching { AnsightRuntime.metric(value = System.currentTimeMillis() % 10_000, channel = 42) }
            renderSnapshot()
        }

        findViewById<Button>(R.id.eventButton).setOnClickListener {
            runCatching {
                AnsightRuntime.event(
                    label = "android_harness_tapped",
                    type = AnsightEventType.Navigation,
                    details = "source=native-harness",
                    channel = 42,
                )
            }
            renderSnapshot()
        }

        findViewById<Button>(R.id.openSessionButton).setOnClickListener {
            runCatching { AnsightRuntime.connect() }
            renderSnapshot()
        }

        findViewById<Button>(R.id.clearButton).setOnClickListener {
            AnsightRuntime.clear()
            renderSnapshot()
        }

        renderSnapshot()
    }

    private fun renderSnapshot() {
        val snapshot = AnsightRuntime.snapshot()
        snapshotView.text = buildString {
            appendLine("initialized=${snapshot.initialized}")
            appendLine("active=${snapshot.active}")
            appendLine("sessionOpen=${snapshot.sessionOpen}")
            appendLine("connection=${snapshot.connectionStatus.connectionState}")
            appendLine("lifecycle=${snapshot.lifecycleState}")
            appendLine("screen=${snapshot.currentScreen?.name ?: "<none>"}")
            appendLine("metricsRecorded=${snapshot.metricsRecorded}")
            appendLine("eventsRecorded=${snapshot.eventsRecorded}")
            appendLine("channels=${snapshot.channels.size}")
            appendLine("appId=${snapshot.deviceProfile?.app?.appId ?: "<none>"}")
            appendLine("registeredTools=${snapshot.registeredTools}")
            appendLine("sessionMessage=${snapshot.sessionMessage ?: "<none>"}")
            appendLine("lastMetric=${snapshot.lastMetric ?: "<none>"}")
            appendLine("lastEvent=${snapshot.lastEvent ?: "<none>"}")
        }
    }
}
