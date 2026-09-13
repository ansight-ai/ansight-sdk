package ai.ansight.runtime.purchases

import android.os.Build

internal object PurchaseEnvironment {
    fun isAllowed(): Boolean = runCatching { isEmulator(Build.HARDWARE) }.getOrDefault(false)
    // Deliberately narrow: debuggable, test-keys and generic fingerprints are not sufficient.
    fun isEmulator(hardware: String?): Boolean = hardware == "goldfish" || hardware == "ranchu"
}

class PurchaseEnvironmentException : IllegalStateException("Purchase interop is restricted to simulators and emulators.") {
    companion object { const val ERROR_CODE = "purchases_environment_not_allowed" }
}
