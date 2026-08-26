// swift-tools-version: 5.9

import PackageDescription

let package = Package(
    name: "ansight_flutter",
    platforms: [
        .iOS(.v15)
    ],
    products: [
        .library(name: "ansight_flutter", targets: ["ansight_flutter"])
    ],
    dependencies: [
        .package(name: "FlutterFramework", path: "../FlutterFramework"),
        .package(
            url: "https://github.com/ansight-ai/ansight-sdk.git",
            exact: "1.4.0-preview.5"
        )
    ],
    targets: [
        .target(
            name: "ansight_flutter",
            dependencies: [
                .product(name: "FlutterFramework", package: "FlutterFramework"),
                .product(name: "Ansight", package: "ansight-sdk")
            ],
            resources: [
                // If your plugin requires a privacy manifest, for example if it uses any required
                // reason APIs, update the PrivacyInfo.xcprivacy file to describe your plugin's
                // privacy impact, and then uncomment these lines. For more information, see
                // https://developer.apple.com/documentation/bundleresources/privacy_manifest_files
                .process("PrivacyInfo.xcprivacy"),

                // If you have other resources that need to be bundled with your plugin, refer to
                // the following instructions to add them:
                // https://developer.apple.com/documentation/xcode/bundling-resources-with-a-swift-package
            ]
        )
    ]
)
