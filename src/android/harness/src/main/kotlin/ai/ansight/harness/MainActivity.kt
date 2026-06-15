package ai.ansight.harness

import android.os.Bundle
import android.util.Base64
import android.widget.Button
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import ai.ansight.runtime.AnsightEventType
import ai.ansight.runtime.AnsightHostConnectionOptions
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.AnsightSessionJpegCaptureOptions
import ai.ansight.runtime.AnsightToolGuard
import ai.ansight.runtime.HostConnectionRequest
import ai.ansight.runtime.HostConnectionRequestKind
import ai.ansight.runtime.HostConnectionResult

class MainActivity : AppCompatActivity() {
    private lateinit var snapshotView: TextView
    private var pairingConfigJson: String? = null
    private var lastConnectionResult: HostConnectionResult? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)

        snapshotView = findViewById(R.id.snapshotView)
        pairingConfigJson = extractPairingConfig()
        runCatching {
            AnsightRuntime.initializeAndActivate(
                application = application,
                options = harnessOptions(pairingConfigJson),
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
            lastConnectionResult = runCatching {
                val pairingConfig = pairingConfigJson
                if (pairingConfig == null) {
                    AnsightRuntime.connect()
                } else {
                    AnsightRuntime.connect(
                        HostConnectionRequest(
                            kind = HostConnectionRequestKind.Payload,
                            payload = pairingConfig,
                        ),
                    )
                }
            }.getOrElse { ex ->
                HostConnectionResult.failure(ex.message ?: "Connection failed.")
            }
            renderSnapshot()
        }

        findViewById<Button>(R.id.clearButton).setOnClickListener {
            AnsightRuntime.clear()
            renderSnapshot()
        }

        renderSnapshot()
    }

    private fun harnessOptions(pairingConfigJson: String?): AnsightOptions = AnsightOptions(
        enableFramesPerSecond = true,
        enableBatteryLevel = true,
        sessionJpegCapture = AnsightSessionJpegCaptureOptions(intervalMilliseconds = 1_000, quality = 70, maxWidth = 720),
        toolGuard = AnsightToolGuard.Full,
        hostConnection = AnsightHostConnectionOptions(bundledDeveloperConfigJson = pairingConfigJson),
    )

    private fun extractPairingConfig(): String? {
        val raw = intent.getStringExtra(EXTRA_PAIRING_CONFIG)?.trim()?.ifBlank { null }
        if (raw != null) {
            return raw
        }

        val encoded = intent.getStringExtra(EXTRA_PAIRING_CONFIG_BASE64)?.trim()?.ifBlank { null }
            ?: return null
        return String(Base64.decode(encoded, Base64.DEFAULT), Charsets.UTF_8).trim().ifBlank { null }
    }

    private fun renderSnapshot() {
        val snapshot = AnsightRuntime.snapshot()
        val connectionResult = lastConnectionResult
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
            appendLine("hasPairingConfig=${pairingConfigJson != null}")
            if (connectionResult != null) {
                appendLine("lastConnectSuccess=${connectionResult.success}")
                appendLine("lastConnectReason=${connectionResult.reasonCode ?: "<none>"}")
                appendLine("lastConnectMessage=${connectionResult.message}")
            }
            appendLine("sessionMessage=${snapshot.sessionMessage ?: "<none>"}")
            appendLine("lastMetric=${snapshot.lastMetric ?: "<none>"}")
            appendLine("lastEvent=${snapshot.lastEvent ?: "<none>"}")
        }
    }

    private companion object {
        const val EXTRA_PAIRING_CONFIG = "ai.ansight.harness.PAIRING_CONFIG"
        const val EXTRA_PAIRING_CONFIG_BASE64 = "ai.ansight.harness.PAIRING_CONFIG_BASE64"
    }
}
