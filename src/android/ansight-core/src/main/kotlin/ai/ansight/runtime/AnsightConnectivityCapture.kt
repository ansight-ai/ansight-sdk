package ai.ansight.runtime

import android.app.Application
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID

data class AnsightConnectivityCaptureOptions(val minimumSampleIntervalMilliseconds: Int = 5_000) {
    fun validated() = copy(minimumSampleIntervalMilliseconds = minimumSampleIntervalMilliseconds.coerceIn(1_000, 300_000))
}

/** Default-path observations only. This monitor never probes hosts or collects network identifiers. */
internal class AnsightConnectivityMonitor(
    private val application: Application,
    private val options: AnsightConnectivityCaptureOptions,
    private val emit: (String, String, String) -> Unit,
) {
    private val handler = Handler(Looper.getMainLooper())
    private val manager = application.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
    private val observationId = UUID.randomUUID().toString()
    private val startedAt = SystemClock.elapsedRealtime()
    @Volatile private var closed = false
    private var sequence = 0L
    private var generation = 0L
    private var running = false
    private var callback: ConnectivityManager.NetworkCallback? = null
    private var receiver: BroadcastReceiver? = null
    private var currentNetwork: Network? = null
    private var snapshot: JSONObject? = null
    private var quality: JSONObject? = null
    private var pendingQuality = false
    private var lastQualityAt = 0L
    private val flushQuality = object : Runnable {
        override fun run() {
            if (!running) return
            if (pendingQuality && quality != null) {
                record("quality", snapshot ?: unknown())
                lastQualityAt = SystemClock.elapsedRealtime()
                pendingQuality = false
            }
            handler.postDelayed(this, options.minimumSampleIntervalMilliseconds.toLong())
        }
    }

    private fun dispatch(action: () -> Unit) {
        if (Looper.myLooper() == handler.looper) action() else handler.post { action() }
    }

    fun resume() { dispatch { if (!closed) start() } }

    fun close() { closed = true; suspend("runtime-reinitialized") }

    fun suspend(reason: String) { dispatch {
        if (!running) return@dispatch
        record("gap", unknown(), reason = reason)
        running = false
        generation++
        callback?.let { runCatching { manager?.unregisterNetworkCallback(it) } }
        receiver?.let { runCatching { application.unregisterReceiver(it) } }
        callback = null
        receiver = null
        handler.removeCallbacks(flushQuality)
        snapshot = null
        quality = null
        pendingQuality = false
        currentNetwork = null
    } }

    private fun start() {
        if (running) return
        running = true
        val token = ++generation
        snapshot = unknown()
        record("snapshot", snapshot!!, reason = "observation-started")
        try {
            if (Build.VERSION.SDK_INT >= 24 && manager != null) {
                val listener = object : ConnectivityManager.NetworkCallback() {
                    override fun onCapabilitiesChanged(network: Network, capabilities: NetworkCapabilities) {
                        handler.post { if (running && token == generation) {
                            currentNetwork = network
                            observe(capabilities)
                        } }
                    }
                    override fun onLost(network: Network) {
                        handler.post { if (running && token == generation && currentNetwork == network) {
                            currentNetwork = null
                            observe(null)
                        } }
                    }
                }
                callback = listener
                manager.registerDefaultNetworkCallback(listener)
            } else {
                val listener = object : BroadcastReceiver() {
                    override fun onReceive(context: Context?, intent: Intent?) {
                        if (running && token == generation) refresh()
                    }
                }
                receiver = listener
                @Suppress("DEPRECATION")
                application.registerReceiver(listener, IntentFilter(ConnectivityManager.CONNECTIVITY_ACTION))
            }
            refresh()
        } catch (_: SecurityException) {
            record("snapshot", unknown(), reason = "network-state-permission-unavailable")
        } catch (_: RuntimeException) {
            record("snapshot", unknown(), reason = "network-state-unavailable")
        }
        handler.postDelayed(flushQuality, options.minimumSampleIntervalMilliseconds.toLong())
    }

    private fun refresh() {
        if (manager == null) return
        if (Build.VERSION.SDK_INT >= 23) {
            currentNetwork = manager.activeNetwork
            observe(currentNetwork?.let { manager.getNetworkCapabilities(it) })
        } else {
            @Suppress("DEPRECATION") val info = manager.activeNetworkInfo
            @Suppress("DEPRECATION") val transport = when (info?.type) {
                ConnectivityManager.TYPE_WIFI -> "wifi"
                ConnectivityManager.TYPE_MOBILE -> "cellular"
                ConnectivityManager.TYPE_ETHERNET -> "ethernet"
                ConnectivityManager.TYPE_BLUETOOTH -> "bluetooth"
                else -> "other"
            }
            @Suppress("DEPRECATION") val available = info?.isConnected == true
            quality = null
            accept(JSONObject().put("availability", if (available) "available" else "unavailable")
                .put("transports", JSONArray(if (available) listOf(transport) else emptyList<String>()))
                .put("metered", manager.isActiveNetworkMetered))
        }
    }

    private fun observe(capabilities: NetworkCapabilities?) {
        if (Build.VERSION.SDK_INT < 23) return
        quality = null
        if (capabilities == null) {
            // A path exists but capabilities are not yet known: do not fabricate a disconnect.
            accept(if (currentNetwork == null) JSONObject().put("availability", "unavailable").put("transports", JSONArray()) else unknown())
            return
        }
        val transports = JSONArray()
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) transports.put("wifi")
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)) transports.put("cellular")
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)) transports.put("ethernet")
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_BLUETOOTH)) transports.put("bluetooth")
        val state = JSONObject().put("availability", "available").put("transports", transports)
            .put("vpn", capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN))
            .put("internetValidated", capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED))
            .put("captivePortal", capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_CAPTIVE_PORTAL))
            .put("metered", !capabilities.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED))
        val sample = JSONObject().put("source", "android.network-capabilities").put("measuredAtUtc", AnsightClock.isoNow())
        if (capabilities.linkDownstreamBandwidthKbps > 0) sample.put("estimatedDownstreamKbps", capabilities.linkDownstreamBandwidthKbps)
        if (capabilities.linkUpstreamBandwidthKbps > 0) sample.put("estimatedUpstreamKbps", capabilities.linkUpstreamBandwidthKbps)
        if (Build.VERSION.SDK_INT >= 29 && capabilities.signalStrength != Int.MIN_VALUE) {
            sample.put("signalStrength", capabilities.signalStrength)
            sample.put("signalStrengthUnit", "bearer-specific")
        }
        if (sample.length() > 2) quality = sample
        accept(state)
    }

    private fun accept(state: JSONObject) {
        val previous = snapshot
        snapshot = state
        if (previous == null || previous.toString() != state.toString()) {
            record(if (previous == null) "snapshot" else "change", state, previous)
            pendingQuality = false
            lastQualityAt = SystemClock.elapsedRealtime()
        } else if (quality != null) {
            if (SystemClock.elapsedRealtime() - lastQualityAt >= options.minimumSampleIntervalMilliseconds) {
                record("quality", state)
                lastQualityAt = SystemClock.elapsedRealtime()
                pendingQuality = false
            } else pendingQuality = true
        }
    }

    private fun unknown() = JSONObject().put("availability", "unknown").put("transports", JSONArray())

    private fun record(kind: String, state: JSONObject, previous: JSONObject? = null, reason: String? = null) {
        val supported = mutableListOf("availability", "transports", "metered")
        if (Build.VERSION.SDK_INT >= 23) supported.addAll(listOf("vpn", "internetValidated", "captivePortal", "estimatedDownstreamKbps", "estimatedUpstreamKbps"))
        if (Build.VERSION.SDK_INT >= 29) supported.add("signalStrength")
        val id = UUID.randomUUID().toString()
        val record = JSONObject().put("schema", "ansight.connectivity.v1").put("id", id)
            .put("observationId", observationId).put("sequence", ++sequence).put("capturedAtUtc", AnsightClock.isoNow())
            .put("elapsedMilliseconds", SystemClock.elapsedRealtime() - startedAt).put("kind", kind)
            .put("source", "android.connectivity-manager").put("snapshot", state)
            .put("supportedMeasurements", JSONArray(supported)).put("previousSnapshot", previous)
            .put("quality", if (kind == "gap") null else quality).put("reason", reason)
        if (!closed) emit("connectivity.$kind", record.toString(), id)
    }
}
