package ai.ansight.motion

import ai.ansight.runtime.AnsightRuntime

/** Forwards samples already observed by the app; does not register a sensor listener. */
object AnsightMotion {
    @JvmStatic
    @JvmOverloads
    fun recordShake(source: String = "app") = AnsightRuntime.recordShake(source)

    @JvmStatic
    fun recordAccelerometer(x: Double, y: Double, z: Double) =
        AnsightRuntime.recordAccelerometerSample(x, y, z)
}
