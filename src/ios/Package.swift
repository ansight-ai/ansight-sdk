// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AnsightSDK",
    platforms: [
        .iOS(.v15),
        .macOS(.v11),
    ],
    products: [
        .library(
            name: "Ansight",
            targets: ["Ansight"]
        ),
        .library(
            name: "AnsightCore",
            targets: ["AnsightCore"]
        ),
        .library(
            name: "AnsightPairingQR",
            targets: ["AnsightPairingQR"]
        ),
        .library(
            name: "AnsightToolsPreferences",
            targets: ["AnsightToolsPreferences"]
        ),
        .library(
            name: "AnsightToolsFileSystem",
            targets: ["AnsightToolsFileSystem"]
        ),
        .library(
            name: "AnsightToolsDatabase",
            targets: ["AnsightToolsDatabase"]
        ),
        .library(
            name: "AnsightToolsSecureStorage",
            targets: ["AnsightToolsSecureStorage"]
        ),
        .library(
            name: "AnsightToolsVisualTree",
            targets: ["AnsightToolsVisualTree"]
        ),
    ],
    targets: [
        .target(
            name: "Ansight",
            dependencies: [
                "AnsightCore",
                "AnsightPairingQR",
                "AnsightToolsDatabase",
                "AnsightToolsFileSystem",
                "AnsightToolsPreferences",
                "AnsightToolsSecureStorage",
                "AnsightToolsVisualTree",
            ],
            path: "Sources/Ansight"
        ),
        .target(
            name: "AnsightCore",
            path: "Sources/AnsightCore",
            plugins: [
                .plugin(name: "AnsightBuildToolPlugin"),
            ]
        ),
        .target(
            name: "AnsightPairingQR",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightPairingQR"
        ),
        .target(
            name: "AnsightToolsPreferences",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsPreferences"
        ),
        .target(
            name: "AnsightToolsFileSystem",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsFileSystem"
        ),
        .target(
            name: "AnsightToolsDatabase",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsDatabase",
            linkerSettings: [
                .linkedLibrary("sqlite3"),
            ]
        ),
        .target(
            name: "AnsightToolsSecureStorage",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsSecureStorage"
        ),
        .target(
            name: "AnsightToolsVisualTree",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsVisualTree"
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
            name: "AnsightCoreTests",
            dependencies: [
                "Ansight",
                "AnsightCore",
                "AnsightPairingQR",
                "AnsightToolsDatabase",
                "AnsightToolsFileSystem",
                "AnsightToolsPreferences",
                "AnsightToolsSecureStorage",
                "AnsightToolsVisualTree",
            ],
            path: "Tests/AnsightCoreTests"
        ),
    ]
)
