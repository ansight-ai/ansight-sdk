import Foundation

#if canImport(Darwin)
import Darwin
#endif

#if canImport(UIKit)
import UIKit
#endif

#if canImport(Metal)
import Metal
#endif

#if canImport(Network)
import Network
#endif

public enum AnsightDeviceAppProfileCollector {
    private static let maxApplicationIconPixelLength = 256
    private static let maxApplicationIconByteCount = 2 * 1_024 * 1_024

    public static func collect(reasonCode: Int = 1, profileSeq: Int = 1) -> DeviceAppProfile {
        DeviceAppProfile(
            sentAt: Int64(Date().timeIntervalSince1970 * 1_000),
            reasonCode: reasonCode,
            profileSeq: profileSeq,
            sdk: DeviceSdkProfile(
                name: AnsightSDKInfo.name,
                packageId: AnsightSDKInfo.packageId,
                version: AnsightSDKInfo.version,
                language: AnsightSDKInfo.language
            ),
            device: collectDeviceProfile(),
            app: collectApplicationProfile(),
            runtime: collectRuntimeProfile(),
            graphics: collectGraphicsProfile(),
            permissions: nil,
            tags: ["ios", "native"]
        )
    }

    private static func collectApplicationProfile() -> DeviceApplicationProfile {
        let bundle = Bundle.main
        let info = bundle.infoDictionary ?? [:]
        return DeviceApplicationProfile(
            appId: bundle.bundleIdentifier,
            appName: (info["CFBundleDisplayName"] as? String) ?? (info["CFBundleName"] as? String),
            icon: collectApplicationIconProfile(bundle: bundle),
            processId: Int(ProcessInfo.processInfo.processIdentifier),
            versionName: info["CFBundleShortVersionString"] as? String,
            versionCode: info["CFBundleVersion"] as? String,
            buildNumber: info["CFBundleVersion"] as? String,
            environmentCode: isDebugBuild ? 3 : 1,
            installSource: nil,
            firstInstallTimeMs: nil,
            lastUpdateTimeMs: nil,
            debuggable: isDebugBuild
        )
    }

    private static func collectDeviceProfile() -> DeviceProfile {
        let storage = storageCapacity()
        return DeviceProfile(
            manufacturer: "Apple",
            brand: "Apple",
            model: hardwareModel(),
            product: simulatorProductName() ?? hardwareModel(),
            formFactor: formFactor(),
            deviceClassCode: deviceClassCode(),
            isVirtual: isSimulator,
            isEmulator: isSimulator,
            locale: Locale.current.identifier,
            timeZone: TimeZone.current.identifier,
            osName: osName,
            osVersion: ProcessInfo.processInfo.operatingSystemVersionString,
            osBuild: sysctlString("kern.osversion"),
            apiLevel: nil,
            cpuArch: cpuArchitecture,
            cpuCoreCount: ProcessInfo.processInfo.processorCount,
            abiList: [cpuArchitecture],
            chipModel: nil,
            memoryTotalMb: Int64(ProcessInfo.processInfo.physicalMemory / 1_048_576),
            memoryFreeMb: nil,
            storageTotalMb: storage.totalMb,
            storageFreeMb: storage.freeMb,
            battery: batteryProfile(),
            display: displayProfile(),
            gpu: gpuProfile(),
            network: networkProfile(),
            thermal: thermalProfile()
        )
    }

    private static func collectRuntimeProfile() -> DeviceRuntimeProfile {
        DeviceRuntimeProfile(
            primary: platformRuntimeCode,
            primaryVersion: osVersion,
            engine: DeviceRuntimeEngineProfile(name: "Swift", version: swiftVersionLabel, metadata: nil),
            stack: [
                DeviceRuntimeStackEntry(runtimeCode: 250, name: "Swift", version: swiftVersionLabel, layer: "language"),
                DeviceRuntimeStackEntry(runtimeCode: platformRuntimeCode, name: osName, version: osVersion, layer: "platform"),
            ],
            aotEnabled: true,
            jitEnabled: false
        )
    }

    private static func collectGraphicsProfile() -> DeviceGraphicsProfile {
        DeviceGraphicsProfile(
            renderBackendCode: gpuProfile() == nil ? nil : 3,
            fpsTarget: displayProfile()?.refreshRateHz.map { Int($0.rounded()) },
            vsyncEnabled: true
        )
    }

    private static func collectApplicationIconProfile(bundle: Bundle) -> DeviceApplicationIconProfile? {
        #if canImport(UIKit)
        for iconName in applicationIconNames(bundle: bundle) {
            if let image = UIImage(named: iconName, in: bundle, compatibleWith: nil),
               let profile = makeApplicationIconProfile(image: image) {
                return profile
            }
        }

        for imageUrl in applicationIconImageUrls(bundle: bundle) {
            if let image = UIImage(contentsOfFile: imageUrl.path),
               let profile = makeApplicationIconProfile(image: image) {
                return profile
            }
        }

        return nil
        #else
        return nil
        #endif
    }

    private static func applicationIconNames(bundle: Bundle) -> [String] {
        var names: [String] = []
        var seen = Set<String>()

        func append(_ value: Any?) {
            guard let raw = value as? String else {
                return
            }

            let name = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !name.isEmpty else {
                return
            }

            let basename = URL(fileURLWithPath: name).deletingPathExtension().lastPathComponent
            for candidate in [name, basename] where !candidate.isEmpty && seen.insert(candidate.lowercased()).inserted {
                names.append(candidate)
            }
        }

        let info = bundle.infoDictionary ?? [:]
        append(info["CFBundleIconName"])
        append(info["CFBundleIconFile"])
        append(info["XSAppIconAssets"])

        if let icons = info["CFBundleIcons"] as? [String: Any],
           let primaryIcon = icons["CFBundlePrimaryIcon"] as? [String: Any] {
            append(primaryIcon["CFBundleIconName"])
            if let iconFiles = primaryIcon["CFBundleIconFiles"] as? [Any] {
                for iconFile in iconFiles {
                    append(iconFile)
                }
            }
        }

        append("appicon")
        append("AppIcon")
        return names
    }

    private static func applicationIconImageUrls(bundle: Bundle) -> [URL] {
        guard let urls = bundle.urls(forResourcesWithExtension: "png", subdirectory: nil) else {
            return []
        }

        return urls.filter { url in
            let name = url.deletingPathExtension().lastPathComponent.lowercased()
            return name.contains("appicon") || name.contains("app-icon")
        }.sorted { left, right in
            left.lastPathComponent.localizedStandardCompare(right.lastPathComponent) == .orderedAscending
        }
    }

    #if canImport(UIKit)
    private static func makeApplicationIconProfile(image: UIImage) -> DeviceApplicationIconProfile? {
        let sourceWidth: Int
        let sourceHeight: Int
        if let cgImage = image.cgImage {
            sourceWidth = cgImage.width
            sourceHeight = cgImage.height
        } else {
            sourceWidth = Int((image.size.width * image.scale).rounded())
            sourceHeight = Int((image.size.height * image.scale).rounded())
        }

        let dimensions = applicationIconDimensions(width: sourceWidth, height: sourceHeight)
        let encodedImage: UIImage
        if sourceWidth == dimensions.width, sourceHeight == dimensions.height {
            encodedImage = image
        } else {
            encodedImage = renderApplicationIcon(image: image, width: dimensions.width, height: dimensions.height)
        }

        guard let data = encodedImage.pngData(),
              !data.isEmpty,
              data.count <= maxApplicationIconByteCount
        else {
            return nil
        }

        return DeviceApplicationIconProfile(
            format: "png",
            mimeType: "image/png",
            width: dimensions.width,
            height: dimensions.height,
            byteCount: data.count,
            dataBase64: data.base64EncodedString()
        )
    }

    private static func applicationIconDimensions(width: Int, height: Int) -> (width: Int, height: Int) {
        guard width > 0, height > 0 else {
            return (maxApplicationIconPixelLength, maxApplicationIconPixelLength)
        }

        let longestSide = max(width, height)
        guard longestSide > maxApplicationIconPixelLength else {
            return (width, height)
        }

        let scale = Double(maxApplicationIconPixelLength) / Double(longestSide)
        return (
            max(1, Int((Double(width) * scale).rounded())),
            max(1, Int((Double(height) * scale).rounded()))
        )
    }

    private static func renderApplicationIcon(image: UIImage, width: Int, height: Int) -> UIImage {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        format.opaque = false
        return UIGraphicsImageRenderer(size: CGSize(width: width, height: height), format: format).image { _ in
            image.draw(in: CGRect(x: 0, y: 0, width: width, height: height))
        }
    }
    #endif

    private static var osName: String {
        #if os(iOS)
        return "ios"
        #elseif os(macOS)
        return "macos"
        #elseif os(tvOS)
        return "tvos"
        #elseif os(watchOS)
        return "watchos"
        #else
        return "apple"
        #endif
    }

    private static var osVersion: String {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        return "\(version.majorVersion).\(version.minorVersion).\(version.patchVersion)"
    }

    private static var platformRuntimeCode: Int {
        #if os(iOS)
        return 2
        #elseif os(tvOS)
        return 2
        #elseif os(watchOS)
        return 2
        #else
        return 250
        #endif
    }

    private static var isSimulator: Bool {
        #if targetEnvironment(simulator)
        return true
        #else
        return false
        #endif
    }

    private static var isDebugBuild: Bool {
        #if DEBUG
        return true
        #else
        return false
        #endif
    }

    private static var cpuArchitecture: String {
        #if arch(arm64)
        return "arm64"
        #elseif arch(x86_64)
        return "x86_64"
        #elseif arch(arm)
        return "arm"
        #else
        return "unknown"
        #endif
    }

    private static var swiftVersionLabel: String {
        #if swift(>=6.0)
        return "6"
        #else
        return "5"
        #endif
    }

    private static func hardwareModel() -> String? {
        #if canImport(Darwin)
        var systemInfo = utsname()
        uname(&systemInfo)
        let mirror = Mirror(reflecting: systemInfo.machine)
        let identifier = mirror.children.reduce(into: "") { result, element in
            guard let value = element.value as? Int8, value != 0 else {
                return
            }

            result.append(String(UnicodeScalar(UInt8(value))))
        }
        return identifier.isEmpty ? nil : identifier
        #else
        return nil
        #endif
    }

    private static func simulatorProductName() -> String? {
        ProcessInfo.processInfo.environment["SIMULATOR_DEVICE_NAME"]
            ?? ProcessInfo.processInfo.environment["SIMULATOR_MODEL_IDENTIFIER"]
    }

    private static func formFactor() -> String? {
        #if canImport(UIKit)
        switch UIDevice.current.userInterfaceIdiom {
        case .phone:
            return "phone"
        case .pad:
            return "tablet"
        case .mac:
            return "desktop"
        case .tv:
            return "tv"
        case .carPlay:
            return "car"
        case .unspecified:
            return nil
        @unknown default:
            return nil
        }
        #elseif os(macOS)
        return "desktop"
        #else
        return nil
        #endif
    }

    private static func deviceClassCode() -> Int? {
        switch formFactor() {
        case "phone":
            return 1
        case "tablet":
            return 2
        case "desktop":
            return 3
        case "tv":
            return 4
        case "watch":
            return 5
        default:
            return nil
        }
    }

    private static func displayProfile() -> DeviceDisplayProfile? {
        #if canImport(UIKit)
        let screen = UIScreen.main
        let bounds = screen.bounds
        let scale = screen.scale
        return DeviceDisplayProfile(
            widthPx: Int((bounds.width * scale).rounded()),
            heightPx: Int((bounds.height * scale).rounded()),
            densityDpi: Int((scale * 160).rounded()),
            refreshRateHz: Double(screen.maximumFramesPerSecond),
            hdrSupported: nil
        )
        #else
        return nil
        #endif
    }

    private static func gpuProfile() -> DeviceGpuProfile? {
        #if canImport(Metal)
        guard let device = MTLCreateSystemDefaultDevice() else {
            return nil
        }

        return DeviceGpuProfile(
            vendor: "Apple",
            model: device.name,
            driver: nil,
            renderer: device.name,
            apiCode: 3,
            driverVersion: nil,
            vramMb: nil,
            featureLevel: metalFeatureLevel(device)
        )
        #else
        return nil
        #endif
    }

    #if canImport(Metal)
    private static func metalFeatureLevel(_ device: MTLDevice) -> String? {
        var families: [String] = []
        #if os(iOS) || os(tvOS)
        if device.supportsFamily(.apple9) { families.append("apple9") }
        if device.supportsFamily(.apple8) { families.append("apple8") }
        if device.supportsFamily(.apple7) { families.append("apple7") }
        if device.supportsFamily(.apple6) { families.append("apple6") }
        if device.supportsFamily(.apple5) { families.append("apple5") }
        if device.supportsFamily(.apple4) { families.append("apple4") }
        #endif
        #if os(macOS) || targetEnvironment(macCatalyst)
        if device.supportsFamily(.mac2) { families.append("mac2") }
        if device.supportsFamily(.mac1) { families.append("mac1") }
        #endif
        if device.supportsFamily(.common3) { families.append("common3") }
        if device.supportsFamily(.common2) { families.append("common2") }
        if device.supportsFamily(.common1) { families.append("common1") }

        return families.isEmpty ? nil : families.joined(separator: ",")
    }
    #endif

    private static func networkProfile() -> DeviceNetworkProfile? {
        #if canImport(Network)
        let path = currentNetworkPathSnapshot()
        let transportCode: Int
        let effectiveType: String?

        if path.status == .unsatisfied {
            transportCode = 1
            effectiveType = "none"
        } else if path.usesInterfaceType(.wifi) {
            transportCode = 2
            effectiveType = "wifi"
        } else if path.usesInterfaceType(.cellular) {
            transportCode = 3
            effectiveType = "cellular"
        } else if path.usesInterfaceType(.wiredEthernet) {
            transportCode = 4
            effectiveType = "ethernet"
        } else if path.usesInterfaceType(.loopback) {
            transportCode = 0
            effectiveType = "loopback"
        } else if path.usesInterfaceType(.other) {
            transportCode = 0
            effectiveType = "other"
        } else {
            transportCode = path.status == .satisfied ? 0 : 1
            effectiveType = nil
        }

        return DeviceNetworkProfile(
            transportCode: transportCode,
            metered: path.isExpensive,
            effectiveType: effectiveType,
            rttMs: nil,
            downKbps: nil
        )
        #else
        return nil
        #endif
    }

    #if canImport(Network)
    private static func currentNetworkPathSnapshot() -> NWPath {
        let monitor = NWPathMonitor()
        let queue = DispatchQueue(label: "ai.ansight.device-profile.network-path")
        monitor.start(queue: queue)
        Thread.sleep(forTimeInterval: 0.05)
        monitor.cancel()
        return monitor.currentPath
    }
    #endif

    private static func batteryProfile() -> DeviceBatteryProfile? {
        #if canImport(UIKit)
        let device = UIDevice.current
        let previous = device.isBatteryMonitoringEnabled
        device.isBatteryMonitoringEnabled = true
        defer {
            if !previous {
                device.isBatteryMonitoringEnabled = false
            }
        }

        let level = device.batteryLevel >= 0 ? Int((device.batteryLevel * 100).rounded()) : nil
        let stateCode: Int?
        switch device.batteryState {
        case .unknown:
            stateCode = nil
        case .unplugged:
            stateCode = 1
        case .charging:
            stateCode = 2
        case .full:
            stateCode = 3
        @unknown default:
            stateCode = nil
        }

        if level == nil && stateCode == nil {
            return nil
        }

        return DeviceBatteryProfile(levelPct: level, stateCode: stateCode, healthCode: nil, temperatureC: nil)
        #else
        return nil
        #endif
    }

    private static func thermalProfile() -> DeviceThermalProfile? {
        let code: Int
        switch ProcessInfo.processInfo.thermalState {
        case .nominal:
            code = 1
        case .fair:
            code = 2
        case .serious:
            code = 3
        case .critical:
            code = 4
        @unknown default:
            return nil
        }

        return DeviceThermalProfile(statusCode: code)
    }

    private static func storageCapacity() -> (totalMb: Int64?, freeMb: Int64?) {
        do {
            let attributes = try FileManager.default.attributesOfFileSystem(forPath: NSHomeDirectory())
            let total = attributes[.systemSize] as? NSNumber
            let free = attributes[.systemFreeSize] as? NSNumber
            return (
                total.map { $0.int64Value / 1_048_576 },
                free.map { $0.int64Value / 1_048_576 }
            )
        } catch {
            return (nil, nil)
        }
    }

    private static func sysctlString(_ name: String) -> String? {
        #if canImport(Darwin)
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else {
            return nil
        }

        var buffer = [CChar](repeating: 0, count: size)
        guard sysctlbyname(name, &buffer, &size, nil, 0) == 0 else {
            return nil
        }

        let endIndex = buffer.firstIndex(of: 0) ?? buffer.count
        let bytes = buffer[..<endIndex].map { UInt8(bitPattern: $0) }
        return String(decoding: bytes, as: UTF8.self)
        #else
        return nil
        #endif
    }
}
