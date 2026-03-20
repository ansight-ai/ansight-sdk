package ai.ansight.flutter

import ai.ansight.runtime.AnsightEventType
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.AnsightToolDescriptor
import ai.ansight.runtime.PairingOpenOptions
import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel

class AnsightFlutterPlugin : FlutterPlugin, MethodChannel.MethodCallHandler {
    private lateinit var channel: MethodChannel
    private lateinit var binding: FlutterPlugin.FlutterPluginBinding

    override fun onAttachedToEngine(flutterPluginBinding: FlutterPlugin.FlutterPluginBinding) {
        binding = flutterPluginBinding
        channel = MethodChannel(flutterPluginBinding.binaryMessenger, "ansight_flutter")
        channel.setMethodCallHandler(this)
    }

    override fun onDetachedFromEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        channel.setMethodCallHandler(null)
    }

    override fun onMethodCall(call: MethodCall, result: MethodChannel.Result) {
        runCatching {
            when (call.method) {
                "initialize" -> {
                    val application = binding.applicationContext as android.app.Application
                    AnsightRuntime.initialize(application, call.arguments<Map<String, Any?>>()!!.toOptions())
                    result.success(null)
                }
                "activate" -> {
                    AnsightRuntime.activate()
                    result.success(null)
                }
                "deactivate" -> {
                    AnsightRuntime.deactivate()
                    result.success(null)
                }
                "clear" -> {
                    AnsightRuntime.clear()
                    result.success(null)
                }
                "metric" -> {
                    val args = call.arguments<Map<String, Any?>>()!!
                    AnsightRuntime.metric(
                        value = (args["value"] as String).toLong(),
                        channel = (args["channel"] as Number?)?.toInt() ?: 255,
                    )
                    result.success(null)
                }
                "event" -> {
                    val args = call.arguments<Map<String, Any?>>()!!
                    AnsightRuntime.event(
                        label = args["label"] as String,
                        type = (args["type"] as String?)?.let(AnsightEventType::valueOf) ?: AnsightEventType.Info,
                        details = args["details"] as String?,
                        channel = (args["channel"] as Number?)?.toInt() ?: 255,
                        id = args["id"] as String? ?: java.util.UUID.randomUUID().toString(),
                    )
                    result.success(null)
                }
                "openSession" -> {
                    val args = call.arguments<Map<String, Any?>>()!!
                    val options = args["options"] as Map<String, Any?>
                    val sessionResult = AnsightRuntime.openSession(
                        pairingJson = args["pairingJson"] as String,
                        options = PairingOpenOptions(
                            clientName = options["clientName"] as String,
                            manualHostAddress = options["manualHostAddress"] as String,
                            expectedAppId = options["expectedAppId"] as String?,
                            profileOverride = (options["profileOverride"] as Map<String, Any?>? ?: emptyMap())
                                .mapValues { it.value?.toString().orEmpty() },
                        ),
                    )
                    result.success(
                        mapOf(
                            "success" to sessionResult.success,
                            "message" to sessionResult.message,
                            "sessionId" to sessionResult.sessionId,
                        )
                    )
                }
                "completeSession" -> {
                    AnsightRuntime.completeSession()
                    result.success(null)
                }
                "closeSession" -> {
                    AnsightRuntime.closeSession()
                    result.success(null)
                }
                "registerTool" -> {
                    val args = call.arguments<Map<String, Any?>>()!!
                    AnsightRuntime.registerTool(
                        AnsightToolDescriptor(
                            id = args["id"] as String,
                            name = args["name"] as String,
                            scope = args["scope"] as String? ?: "Read",
                        ),
                    )
                    result.success(null)
                }
                "getDebugSnapshot" -> {
                    val snapshot = AnsightRuntime.snapshot()
                    result.success(
                        mapOf(
                            "initialized" to snapshot.initialized,
                            "active" to snapshot.active,
                            "sessionOpen" to snapshot.sessionOpen,
                            "metricsRecorded" to snapshot.metricsRecorded,
                            "eventsRecorded" to snapshot.eventsRecorded,
                            "registeredTools" to snapshot.registeredTools,
                            "sessionMessage" to snapshot.sessionMessage,
                            "lastMetric" to snapshot.lastMetric?.let {
                                mapOf(
                                    "value" to it.value,
                                    "channel" to it.channel,
                                    "capturedAtEpochMs" to it.capturedAtEpochMs,
                                )
                            },
                            "lastEvent" to snapshot.lastEvent?.let {
                                mapOf(
                                    "id" to it.id,
                                    "label" to it.label,
                                    "type" to it.type.name,
                                    "details" to it.details,
                                    "channel" to it.channel,
                                    "capturedAtEpochMs" to it.capturedAtEpochMs,
                                )
                            },
                        )
                    )
                }
                else -> result.notImplemented()
            }
        }.onFailure {
            result.error("ansight_flutter_error", it.message, null)
        }
    }

    private fun Map<String, Any?>.toOptions(): AnsightOptions {
        val channels = (this["additionalChannels"] as List<Map<String, Any?>>? ?: emptyList()).map { channel ->
            ai.ansight.runtime.AnsightChannel(
                id = (channel["id"] as Number).toInt(),
                name = channel["name"] as String,
                colorHex = channel["colorHex"] as String?,
            )
        }

        return AnsightOptions(
            sampleFrequencyMilliseconds = (this["sampleFrequencyMilliseconds"] as Number?)?.toInt() ?: 500,
            retentionPeriodSeconds = (this["retentionPeriodSeconds"] as Number?)?.toInt() ?: 600,
            enableFramesPerSecond = this["enableFramesPerSecond"] as Boolean? ?: true,
            additionalChannels = channels,
        )
    }
}
