// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AnsightKit",
    platforms: [
        .iOS(.v15),
        .macOS(.v11),
    ],
    products: [
        .library(
            name: "AnsightKit",
            targets: ["AnsightKit"]
        ),
    ],
    targets: [
        .target(
            name: "AnsightKit",
            path: "Sources/AnsightKit",
            plugins: [
                .plugin(name: "AnsightBuildToolPlugin"),
            ]
        ),
        .executableTarget(
            name: "AnsightBuildTool",
            path: "Plugins/AnsightBuildTool"
        ),
        .plugin(
            name: "AnsightBuildToolPlugin",
            capability: .buildTool(),
            dependencies: [
                "AnsightBuildTool",
            ]
        ),
        .testTarget(
            name: "AnsightKitTests",
            dependencies: [
                "AnsightKit",
            ],
            path: "Tests/AnsightKitTests"
        ),
    ]
)
