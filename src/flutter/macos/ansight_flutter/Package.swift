// swift-tools-version: 5.9

import PackageDescription
import Foundation

let useLocalAnsightSdk = ProcessInfo.processInfo.environment["ANSIGHT_USE_LOCAL_SDK"] == "1"
let localAnsightSdkPath = ProcessInfo.processInfo.environment["ANSIGHT_LOCAL_SDK_PATH"] ?? "../../../ios"
let ansightSdkDependency: Package.Dependency = useLocalAnsightSdk
    ? .package(name: "AnsightSDK", path: localAnsightSdkPath)
    : .package(
        url: "https://github.com/ansight-ai/ansight-sdk.git",
        exact: "1.6.0"
    )
let ansightTargetDependency: Target.Dependency = .product(
    name: "Ansight",
    package: useLocalAnsightSdk ? "AnsightSDK" : "ansight-sdk"
)

let package = Package(
    name: "ansight_flutter",
    platforms: [
        .macOS(.v10_15)
    ],
    products: [
        .library(name: "ansight-flutter", targets: ["ansight_flutter"])
    ],
    dependencies: [
        .package(name: "FlutterFramework", path: "../FlutterFramework"),
        ansightSdkDependency
    ],
    targets: [
        .target(
            name: "ansight_flutter",
            dependencies: [
                .product(name: "FlutterFramework", package: "FlutterFramework"),
                ansightTargetDependency
            ],
            resources: [
                .process("PrivacyInfo.xcprivacy")
            ]
        )
    ]
)
