package ai.ansight.runtime

import android.app.ActivityManager
import android.app.Application
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ApplicationInfo
import android.content.pm.PackageInfo
import android.net.ConnectivityManager
import android.os.BatteryManager
import android.os.Build
import android.os.Debug
import android.os.Process
import android.os.StatFs
import android.system.Os
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.Locale
import java.util.TimeZone

data class DeviceAppProfile(
    val sentAt: Long,
    val reasonCode: Int,
    val profileSeq: Int,
    val sdk: DeviceSdkProfile,
    val device: DeviceProfile,
    val app: DeviceApplicationProfile,
    val runtime: DeviceRuntimeProfile,
    val graphics: DeviceGraphicsProfile? = null,
    val permissions: Map<String, String> = emptyMap(),
    val tags: List<String> = emptyList(),
) {
    fun toJson(): JSONObject {
        val json = JSONObject()
            .put("type", "DeviceAppProfile")
            .put("schema", "ansight.device-app-profile.v1")
            .put("sentAt", sentAt)
            .put("reasonCode", reasonCode)
            .put("profileSeq", profileSeq)
            .put("sdk", sdk.toJson())
            .put("device", device.toJson())
            .put("app", app.toJson())
            .put("runtime", runtime.toJson())

        if (graphics != null) {
            json.put("graphics", graphics.toJson())
        }
        if (permissions.isNotEmpty()) {
            val permissionJson = JSONObject()
            permissions.entries.sortedBy { it.key }.forEach { permissionJson.put(it.key, it.value) }
            json.put("permissions", permissionJson)
        }
        if (tags.isNotEmpty()) {
            json.put("tags", JSONArray(tags))
        }

        return json
    }
}

data class DeviceSdkProfile(
    val name: String,
    val packageId: String,
    val version: String,
    val language: String,
    val platformFamily: String,
    val capabilities: List<String>,
) {
    fun toJson(): JSONObject = JSONObject()
        .put("name", name)
        .put("packageId", packageId)
        .put("version", version)
        .put("language", language)
        .put("platformFamily", platformFamily)
        .put("capabilities", JSONArray(capabilities))
}

data class DeviceProfile(
    val manufacturer: String?,
    val brand: String?,
    val model: String?,
    val product: String?,
    val formFactor: String?,
    val deviceClassCode: Int?,
    val isVirtual: Boolean?,
    val isEmulator: Boolean?,
    val locale: String?,
    val timeZone: String?,
    val osName: String?,
    val osVersion: String?,
    val osBuild: String?,
    val apiLevel: Int?,
    val cpuArch: String?,
    val cpuCoreCount: Int?,
    val abiList: List<String>,
    val memoryTotalMb: Long?,
    val memoryFreeMb: Long?,
    val storageTotalMb: Long?,
    val storageFreeMb: Long?,
    val battery: DeviceBatteryProfile?,
    val display: DeviceDisplayProfile?,
    val network: DeviceNetworkProfile?,
    val nativeDeviceId: String? = null,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("nativeDeviceId", nativeDeviceId)
        .putIfNotNull("manufacturer", manufacturer)
        .putIfNotNull("brand", brand)
        .putIfNotNull("model", model)
        .putIfNotNull("product", product)
        .putIfNotNull("formFactor", formFactor)
        .putIfNotNull("deviceClassCode", deviceClassCode)
        .putIfNotNull("isVirtual", isVirtual)
        .putIfNotNull("isEmulator", isEmulator)
        .putIfNotNull("locale", locale)
        .putIfNotNull("timeZone", timeZone)
        .putIfNotNull("osName", osName)
        .putIfNotNull("osVersion", osVersion)
        .putIfNotNull("osBuild", osBuild)
        .putIfNotNull("apiLevel", apiLevel)
        .putIfNotNull("cpuArch", cpuArch)
        .putIfNotNull("cpuCoreCount", cpuCoreCount)
        .put("abiList", JSONArray(abiList))
        .putIfNotNull("memoryTotalMb", memoryTotalMb)
        .putIfNotNull("memoryFreeMb", memoryFreeMb)
        .putIfNotNull("storageTotalMb", storageTotalMb)
        .putIfNotNull("storageFreeMb", storageFreeMb)
        .putIfNotNull("battery", battery?.toJson())
        .putIfNotNull("display", display?.toJson())
        .putIfNotNull("network", network?.toJson())
}

data class DeviceApplicationProfile(
    val appId: String?,
    val appName: String?,
    val processId: Int?,
    val versionName: String?,
    val versionCode: String?,
    val buildNumber: String?,
    val environmentCode: Int?,
    val installSource: String?,
    val firstInstallTimeMs: Long?,
    val lastUpdateTimeMs: Long?,
    val debuggable: Boolean?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("appId", appId)
        .putIfNotNull("appName", appName)
        .putIfNotNull("processId", processId)
        .putIfNotNull("versionName", versionName)
        .putIfNotNull("versionCode", versionCode)
        .putIfNotNull("buildNumber", buildNumber)
        .putIfNotNull("environmentCode", environmentCode)
        .putIfNotNull("installSource", installSource)
        .putIfNotNull("firstInstallTimeMs", firstInstallTimeMs)
        .putIfNotNull("lastUpdateTimeMs", lastUpdateTimeMs)
        .putIfNotNull("debuggable", debuggable)
}

data class DeviceRuntimeProfile(
    val primary: Int?,
    val primaryVersion: String?,
    val engine: DeviceRuntimeEngineProfile?,
    val stack: List<DeviceRuntimeStackEntry>,
    val aotEnabled: Boolean?,
    val jitEnabled: Boolean?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("primary", primary)
        .putIfNotNull("primaryVersion", primaryVersion)
        .putIfNotNull("engine", engine?.toJson())
        .put("stack", JSONArray(stack.map { it.toJson() }))
        .putIfNotNull("aotEnabled", aotEnabled)
        .putIfNotNull("jitEnabled", jitEnabled)
}

data class DeviceRuntimeEngineProfile(
    val name: String?,
    val version: String?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("name", name)
        .putIfNotNull("version", version)
}

data class DeviceRuntimeStackEntry(
    val runtimeCode: Int?,
    val name: String,
    val version: String?,
    val layer: String?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("runtimeCode", runtimeCode)
        .put("name", name)
        .putIfNotNull("version", version)
        .putIfNotNull("layer", layer)
}

data class DeviceGraphicsProfile(
    val display: DeviceDisplayProfile?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("display", display?.toJson())
}

data class DeviceBatteryProfile(
    val levelPct: Int?,
    val stateCode: Int?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("levelPct", levelPct)
        .putIfNotNull("stateCode", stateCode)
}

data class DeviceDisplayProfile(
    val widthPx: Int?,
    val heightPx: Int?,
    val densityDpi: Int?,
    val scale: Float?,
    val refreshRate: Float?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("widthPx", widthPx)
        .putIfNotNull("heightPx", heightPx)
        .putIfNotNull("densityDpi", densityDpi)
        .putIfNotNull("scale", scale)
        .putIfNotNull("refreshRate", refreshRate)
}

data class DeviceNetworkProfile(
    val transport: String?,
    val metered: Boolean?,
) {
    fun toJson(): JSONObject = JSONObject()
        .putIfNotNull("transport", transport)
        .putIfNotNull("metered", metered)
}

object DeviceAppProfileCollector {
    private const val androidRuntimeCode = 1
    private const val kotlinRuntimeCode = 250
    private const val javaRuntimeCode = 251

    fun collect(application: Application, profileSeq: Int, reasonCode: Int = 1): DeviceAppProfile {
        val packageInfo = packageInfo(application)
        val display = displayProfile(application)
        return DeviceAppProfile(
            sentAt = System.currentTimeMillis(),
            reasonCode = reasonCode,
            profileSeq = profileSeq,
            sdk = DeviceSdkProfile(
                name = "Ansight Android",
                packageId = "ai.ansight.runtime",
                version = BuildConfig.ANSIGHT_SDK_VERSION,
                language = "kotlin",
                platformFamily = "android",
                capabilities = listOf(
                    "runtime",
                    "pairing",
                    "hostConnection",
                    "liveTransport",
                    "deviceProfile",
                    "telemetry",
                    "lifecycle",
                    "screenViews",
                ),
            ),
            device = DeviceProfile(
                manufacturer = Build.MANUFACTURER.nullIfBlank(),
                brand = Build.BRAND.nullIfBlank(),
                model = Build.MODEL.nullIfBlank(),
                product = Build.PRODUCT.nullIfBlank(),
                formFactor = resolveFormFactor(application),
                deviceClassCode = 1,
                isVirtual = isEmulator(),
                isEmulator = isEmulator(),
                locale = Locale.getDefault().toLanguageTag().nullIfBlank(),
                timeZone = TimeZone.getDefault().id.nullIfBlank(),
                osName = "android",
                osVersion = Build.VERSION.RELEASE.nullIfBlank(),
                osBuild = Build.DISPLAY.nullIfBlank(),
                apiLevel = Build.VERSION.SDK_INT,
                cpuArch = System.getProperty("os.arch").nullIfBlank(),
                cpuCoreCount = java.lang.Runtime.getRuntime().availableProcessors(),
                abiList = Build.SUPPORTED_ABIS?.mapNotNull { it.nullIfBlank() } ?: emptyList(),
                memoryTotalMb = memoryInfo(application)?.totalMem?.bytesToMb(),
                memoryFreeMb = memoryInfo(application)?.availMem?.bytesToMb(),
                storageTotalMb = storageStats(application)?.first,
                storageFreeMb = storageStats(application)?.second,
                battery = batteryProfile(application),
                display = display,
                network = networkProfile(application),
            ),
            app = DeviceApplicationProfile(
                appId = application.packageName.nullIfBlank(),
                appName = applicationLabel(application),
                processId = Process.myPid(),
                versionName = packageInfo?.versionName.nullIfBlank(),
                versionCode = packageInfo?.versionCodeString(),
                buildNumber = packageInfo?.versionCodeString(),
                environmentCode = if (application.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE != 0) 1 else 3,
                installSource = installSource(application),
                firstInstallTimeMs = packageInfo?.firstInstallTime,
                lastUpdateTimeMs = packageInfo?.lastUpdateTime,
                debuggable = application.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE != 0,
            ),
            runtime = DeviceRuntimeProfile(
                primary = androidRuntimeCode,
                primaryVersion = Build.VERSION.RELEASE.nullIfBlank(),
                engine = DeviceRuntimeEngineProfile(
                    name = System.getProperty("java.vm.name").nullIfBlank() ?: "ART",
                    version = System.getProperty("java.vm.version").nullIfBlank(),
                ),
                stack = listOf(
                    DeviceRuntimeStackEntry(kotlinRuntimeCode, "Kotlin", KotlinVersion.CURRENT.toString(), "language"),
                    DeviceRuntimeStackEntry(javaRuntimeCode, "Java", System.getProperty("java.version").nullIfBlank(), "runtime"),
                    DeviceRuntimeStackEntry(androidRuntimeCode, "Android", Build.VERSION.RELEASE.nullIfBlank(), "platform"),
                ),
                aotEnabled = true,
                jitEnabled = true,
            ),
            graphics = DeviceGraphicsProfile(display),
        )
    }

    private fun packageInfo(application: Application): PackageInfo? {
        return try {
            application.packageManager.getPackageInfo(application.packageName, 0)
        } catch (_: Exception) {
            null
        }
    }

    private fun applicationLabel(application: Application): String? {
        return try {
            application.packageManager.getApplicationLabel(application.applicationInfo)?.toString().nullIfBlank()
        } catch (_: Exception) {
            null
        }
    }

    private fun installSource(application: Application): String? {
        return try {
            if (Build.VERSION.SDK_INT >= 30) {
                application.packageManager.getInstallSourceInfo(application.packageName).installingPackageName.nullIfBlank()
            } else {
                @Suppress("DEPRECATION")
                application.packageManager.getInstallerPackageName(application.packageName).nullIfBlank()
            }
        } catch (_: Exception) {
            null
        }
    }

    private fun resolveFormFactor(application: Application): String {
        val configuration = application.resources.configuration
        val uiMode = configuration.uiMode and 0x0f
        return when (uiMode) {
            0x03 -> "car"
            0x04 -> "tv"
            0x06 -> "watch"
            0x07 -> "vr"
            else -> {
                val size = configuration.screenLayout and 0x0f
                if (size >= 0x03) "tablet" else "phone"
            }
        }
    }

    private fun isEmulator(): Boolean {
        val fingerprint = Build.FINGERPRINT.orEmpty()
        val model = Build.MODEL.orEmpty()
        val product = Build.PRODUCT.orEmpty()
        val manufacturer = Build.MANUFACTURER.orEmpty()
        val brand = Build.BRAND.orEmpty()
        val device = Build.DEVICE.orEmpty()
        return fingerprint.contains("generic", ignoreCase = true) ||
            fingerprint.contains("emulator", ignoreCase = true) ||
            model.contains("Emulator", ignoreCase = true) ||
            model.contains("Android SDK built for", ignoreCase = true) ||
            manufacturer.contains("Genymotion", ignoreCase = true) ||
            (brand.startsWith("generic", ignoreCase = true) && device.startsWith("generic", ignoreCase = true)) ||
            product.contains("sdk", ignoreCase = true)
    }

    private fun memoryInfo(application: Application): ActivityManager.MemoryInfo? {
        val manager = application.getSystemService(Context.ACTIVITY_SERVICE) as? ActivityManager ?: return null
        return ActivityManager.MemoryInfo().also { manager.getMemoryInfo(it) }
    }

    private fun storageStats(application: Application): Pair<Long, Long>? {
        return try {
            val stat = StatFs(application.filesDir.absolutePath)
            Pair(
                stat.totalBytes.bytesToMb(),
                stat.availableBytes.bytesToMb(),
            )
        } catch (_: Exception) {
            null
        }
    }

    private fun batteryProfile(application: Application): DeviceBatteryProfile? {
        return try {
            val intent = application.registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED)) ?: return null
            val level = intent.getIntExtra(BatteryManager.EXTRA_LEVEL, -1)
            val scale = intent.getIntExtra(BatteryManager.EXTRA_SCALE, -1)
            val status = intent.getIntExtra(BatteryManager.EXTRA_STATUS, -1)
            DeviceBatteryProfile(
                levelPct = if (level >= 0 && scale > 0) ((level * 100.0) / scale).toInt() else null,
                stateCode = when (status) {
                    BatteryManager.BATTERY_STATUS_CHARGING -> 2
                    BatteryManager.BATTERY_STATUS_FULL -> 3
                    BatteryManager.BATTERY_STATUS_DISCHARGING,
                    BatteryManager.BATTERY_STATUS_NOT_CHARGING -> 1
                    else -> 0
                },
            )
        } catch (_: Exception) {
            null
        }
    }

    private fun displayProfile(application: Application): DeviceDisplayProfile {
        val metrics = application.resources.displayMetrics
        return DeviceDisplayProfile(
            widthPx = metrics.widthPixels,
            heightPx = metrics.heightPixels,
            densityDpi = metrics.densityDpi,
            scale = metrics.density,
            refreshRate = null,
        )
    }

    private fun networkProfile(application: Application): DeviceNetworkProfile? {
        return try {
            val manager = application.getSystemService(Context.CONNECTIVITY_SERVICE) as? ConnectivityManager
                ?: return null
            val activeNetwork = manager.activeNetwork ?: return null
            val capabilities = manager.getNetworkCapabilities(activeNetwork)
            val transport = when {
                capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_WIFI) == true -> "wifi"
                capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_CELLULAR) == true -> "cellular"
                capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_ETHERNET) == true -> "ethernet"
                capabilities?.hasTransport(android.net.NetworkCapabilities.TRANSPORT_VPN) == true -> "vpn"
                else -> "unknown"
            }
            DeviceNetworkProfile(transport = transport, metered = manager.isActiveNetworkMetered)
        } catch (_: Exception) {
            null
        }
    }

    private fun PackageInfo.versionCodeString(): String {
        return if (Build.VERSION.SDK_INT >= 28) {
            longVersionCode.toString()
        } else {
            @Suppress("DEPRECATION")
            versionCode.toString()
        }
    }

    private fun Long.bytesToMb(): Long = this / (1024L * 1024L)

    private fun String?.nullIfBlank(): String? = this?.trim()?.ifBlank { null }
}

internal object AndroidMetricSampler {
    fun javaHeapBytes(): Long {
        val runtime = java.lang.Runtime.getRuntime()
        return runtime.totalMemory() - runtime.freeMemory()
    }

    fun nativeHeapBytes(): Long = Debug.getNativeHeapAllocatedSize()

    fun rssBytes(): Long {
        return try {
            val memoryInfo = Debug.MemoryInfo()
            Debug.getMemoryInfo(memoryInfo)
            memoryInfo.totalPss.toLong() * 1024L
        } catch (_: Exception) {
            0L
        }
    }

    fun openFileHandleCount(): Long? {
        val descriptorDirectory = File("/proc/self/fd")
        val descriptors = descriptorDirectory.list()
            ?.mapNotNull { it.toIntOrNull() }
            ?: return null
        return descriptors.count { descriptor ->
            runCatching {
                Os.stat(File(descriptorDirectory, descriptor.toString()).path)
            }.isSuccess
        }.toLong()
    }
}
