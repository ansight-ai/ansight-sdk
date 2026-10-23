// swift-tools-version: 5.9
import PackageDescription
import Foundation

let useLocalAnsightSdk = ProcessInfo.processInfo.environment["ANSIGHT_USE_LOCAL_SDK"] == "1"
let ansightSdkDependency: Package.Dependency = useLocalAnsightSdk
    ? .package(name: "AnsightSDK", path: "../ios")
    : .package(
        url: "https://github.com/ansight-ai/ansight-sdk.git",
        exact: "1.9.0"
    )
let ansightTargetDependency: Target.Dependency = .product(
    name: "AnsightPencil",
    package: useLocalAnsightSdk ? "AnsightSDK" : "ansight-sdk"
)

let package = Package(
    name: "AnsightCapacitorPencil",
    platforms: [.iOS(.v15)],
    products: [
        .library(name: "AnsightCapacitorPencil", targets: ["AnsightCapacitorPencilPlugin"])
    ],
    dependencies: [
        .package(url: "https://github.com/ionic-team/capacitor-swift-pm.git", from: "8.0.0"),
        ansightSdkDependency
    ],
    targets: [
        .target(
            name: "AnsightCapacitorPencilPlugin",
            dependencies: [
                .product(name: "Capacitor", package: "capacitor-swift-pm"),
                .product(name: "Cordova", package: "capacitor-swift-pm"),
                ansightTargetDependency
            ],
            path: "ios/Sources/AnsightCapacitorPencilPlugin"
        )
    ]
)
