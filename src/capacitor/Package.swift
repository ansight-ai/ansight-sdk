// swift-tools-version: 5.9
import PackageDescription
import Foundation

let useLocalAnsightSdk = ProcessInfo.processInfo.environment["ANSIGHT_USE_LOCAL_SDK"] == "1"
let ansightSdkDependency: Package.Dependency = useLocalAnsightSdk
    ? .package(name: "AnsightSDK", path: "../ios")
    : .package(
        url: "https://github.com/ansight-ai/ansight-sdk.git",
        exact: "1.4.0-preview.1"
    )
let ansightTargetDependency: Target.Dependency = .product(
    name: "Ansight",
    package: useLocalAnsightSdk ? "AnsightSDK" : "ansight-sdk"
)

let package = Package(
    name: "AnsightCapacitor",
    platforms: [.iOS(.v15), .macOS(.v11)],
    products: [
        .library(name: "AnsightCapacitor", targets: ["AnsightCapacitorPlugin"])
    ],
    dependencies: [
        .package(url: "https://github.com/ionic-team/capacitor-swift-pm.git", from: "8.0.0"),
        ansightSdkDependency
    ],
    targets: [
        .target(
            name: "AnsightCapacitorPlugin",
            dependencies: [
                .product(name: "Capacitor", package: "capacitor-swift-pm"),
                .product(name: "Cordova", package: "capacitor-swift-pm"),
                ansightTargetDependency
            ],
            path: "ios/Sources/AnsightCapacitorPlugin"
        )
    ]
)
