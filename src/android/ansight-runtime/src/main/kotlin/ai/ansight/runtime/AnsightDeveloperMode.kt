package ai.ansight.runtime

import android.app.Application

object AnsightDeveloperMode {
    @JvmStatic
    @JvmOverloads
    fun options(
        bundledDeveloperConfigJson: String? = null,
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
        toolGuard = AnsightToolGuard.Full,
        hostAutoProbe = AnsightHostAutoProbeOptions(
            enabled = true,
            clientName = clientName,
        ),
        hostConnection = AnsightHostConnectionOptions(
            bundledDeveloperConfigJson = bundledDeveloperConfigJson,
        ),
    )

    @JvmStatic
    @JvmOverloads
    fun initializeAndActivateAnsightSdk(
        application: Application,
        bundledDeveloperConfigJson: String? = null,
        clientName: String? = null,
    ) {
        AnsightRuntime.initializeAndActivate(
            application = application,
            options = options(
                bundledDeveloperConfigJson = bundledDeveloperConfigJson,
                clientName = clientName,
            ),
        )
    }
}
