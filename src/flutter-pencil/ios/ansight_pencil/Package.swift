// swift-tools-version: 5.9

import PackageDescription
import Foundation

let useLocalAnsightSdk = ProcessInfo.processInfo.environment["ANSIGHT_USE_LOCAL_SDK"] == "1"
let localAnsightSdkPath = ProcessInfo.processInfo.environment["ANSIGHT_LOCAL_SDK_PATH"] ?? "../../../ios"
let ansightSdkDependency: Package.Dependency = useLocalAnsightSdk
    ? .package(name: "AnsightSDK", path: localAnsightSdkPath)
    : .package(
        url: "https://github.com/ansight-ai/ansight-sdk.git",
        exact: "1.8.1"
    )
let ansightTargetDependency: Target.Dependency = .product(
    name: "AnsightPencil",
    package: useLocalAnsightSdk ? "AnsightSDK" : "ansight-sdk"
)

let package = Package(
    name: "ansight_pencil",
    platforms: [
        .iOS(.v15)
    ],
    products: [
        .library(name: "ansight_pencil", targets: ["ansight_pencil"])
    ],
    dependencies: [
        .package(name: "FlutterFramework", path: "../FlutterFramework"),
        ansightSdkDependency
    ],
    targets: [
        .target(
            name: "ansight_pencil",
            dependencies: [
                .product(name: "FlutterFramework", package: "FlutterFramework"),
                ansightTargetDependency
            ]
        )
    ]
)
