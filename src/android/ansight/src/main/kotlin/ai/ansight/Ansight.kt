package ai.ansight

import ai.ansight.pairing.AnsightPairing
import ai.ansight.runtime.AndroidTool
import ai.ansight.runtime.AnsightDeveloperMode
import ai.ansight.runtime.AnsightHostConnectionOptions
import ai.ansight.runtime.AnsightOptions
import ai.ansight.runtime.AnsightRuntime
import ai.ansight.runtime.HostConnectionResult
import ai.ansight.runtime.OperationResult
import android.app.Application
import android.app.Activity
import com.google.android.material.bottomsheet.BottomSheetDialog

object Ansight {
    @JvmStatic
    fun standardTools(): List<AndroidTool> = AnsightStandardTools.create()

    @JvmStatic
    @JvmOverloads
    fun options(baseOptions: AnsightOptions = AnsightOptions()): AnsightOptions = withStandardTools(baseOptions)

    @JvmStatic
    fun options(hostConnection: AnsightHostConnectionOptions): AnsightOptions = options(
        AnsightOptions(hostConnection = hostConnection),
    )

    @JvmStatic
    @JvmOverloads
    fun developerOptions(
        bundledDeveloperConfigJson: String? = null,
        clientName: String? = null,
    ): AnsightOptions = withStandardTools(
        AnsightDeveloperMode.options(
            bundledDeveloperConfigJson = bundledDeveloperConfigJson,
            clientName = clientName,
        ),
    )

    @JvmStatic
    @JvmOverloads
    fun initialize(application: Application, options: AnsightOptions = options()) {
        AnsightRuntime.initialize(application, options)
    }

    @JvmStatic
    @JvmOverloads
    fun initializeAndActivate(application: Application, options: AnsightOptions = options()) {
        AnsightRuntime.initializeAndActivate(application, options)
    }

    @JvmStatic
    @JvmOverloads
    fun initializeAndActivateDeveloperMode(
        application: Application,
        bundledDeveloperConfigJson: String? = null,
        clientName: String? = null,
    ) {
        AnsightRuntime.initializeAndActivate(
            application = application,
            options = developerOptions(
                bundledDeveloperConfigJson = bundledDeveloperConfigJson,
                clientName = clientName,
            ),
        )
    }

    @JvmStatic
    fun updateCustomProperties(customProperties: Map<String, Map<String, String>>): OperationResult {
        return AnsightRuntime.updateCustomProperties(customProperties)
    }

    @JvmStatic
    fun registerCustomProperty(group: String, key: String, value: String): OperationResult {
        return AnsightRuntime.registerCustomProperty(group, key, value)
    }

    @JvmStatic
    fun removeCustomProperty(group: String, key: String): OperationResult {
        return AnsightRuntime.removeCustomProperty(group, key)
    }

    @JvmStatic
    fun clearCustomProperties(): OperationResult {
        return AnsightRuntime.clearCustomProperties()
    }

    @JvmStatic
    @JvmOverloads
    fun showPairingSheet(
        activity: Activity,
        clientName: String? = null,
        expectedAppId: String? = activity.packageName,
        hostAddressOverride: String? = null,
        onResult: (HostConnectionResult) -> Unit = {},
        onError: (Throwable) -> Unit = {},
    ): BottomSheetDialog = AnsightPairing.showPairingSheet(
        activity = activity,
        clientName = clientName,
        expectedAppId = expectedAppId,
        hostAddressOverride = hostAddressOverride,
        onResult = onResult,
        onError = onError,
    )

    private fun withStandardTools(options: AnsightOptions): AnsightOptions {
        val existingIds = options.initialTools.map { it.definition.id }.toSet()
        val additionalTools = standardTools().filter { it.definition.id !in existingIds }
        return options.copy(initialTools = options.initialTools + additionalTools)
    }
}
