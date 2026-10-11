package ai.ansight.connectivity

import ai.ansight.runtime.AnsightConnectivityCaptureOptions
import ai.ansight.runtime.AnsightOptionsBuilder

/** Optional connectivity feature facade. Collection starts with runtime activation. */
object AnsightConnectivity {
    @JvmStatic
    @JvmOverloads
    fun configure(builder: AnsightOptionsBuilder, options: AnsightConnectivityCaptureOptions = AnsightConnectivityCaptureOptions()): AnsightOptionsBuilder = builder.withConnectivityCapture(options)
}
