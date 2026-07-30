package ai.ansight.runtime

import android.app.Application

object AnsightDeveloperMode {
    @JvmStatic
    @JvmOverloads
    fun options(
        clientName: String? = null,
    ): AnsightOptions = AnsightOptions(
        sampleFrequencyMilliseconds = 400,
        retentionPeriodSeconds = 120,
        enableFramesPerSecond = true,
        enableBatteryLevel = false,
        sessionJpegCapture = AnsightSessionJpegCaptureOptions(
            intervalMilliseconds = 2_000,
            quality = 60,
            maxWidth = 480,
        ),
        touchCapture = AnsightTouchCaptureOptions(),
        toolGuard = AnsightToolGuard.FullAccess,
        hostAutoProbe = AnsightHostAutoProbeOptions(
            enabled = true,
            clientName = clientName,
        ),
    )

    @JvmStatic
    @JvmOverloads
    fun initializeAndActivateAnsightSdk(
        application: Application,
        clientName: String? = null,
    ) {
        AnsightRuntime.initializeAndActivate(
            application = application,
            options = options(
                clientName = clientName,
            ),
        )
    }
}
