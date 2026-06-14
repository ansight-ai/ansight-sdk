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
            name: "Ansight",
            targets: ["Ansight"]
        ),
        .library(
            name: "AnsightKit",
            targets: ["AnsightKit"]
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
                "AnsightKit",
                "AnsightToolsDatabase",
                "AnsightToolsFileSystem",
                "AnsightToolsPreferences",
                "AnsightToolsSecureStorage",
                "AnsightToolsVisualTree",
            ],
            path: "Sources/Ansight"
        ),
        .target(
            name: "AnsightKit",
            path: "Sources/AnsightKit",
            plugins: [
                .plugin(name: "AnsightBuildToolPlugin"),
            ]
        ),
        .target(
            name: "AnsightToolsPreferences",
            dependencies: [
                "AnsightKit",
            ],
            path: "Sources/AnsightToolsPreferences"
        ),
        .target(
            name: "AnsightToolsFileSystem",
            dependencies: [
                "AnsightKit",
            ],
            path: "Sources/AnsightToolsFileSystem"
        ),
        .target(
            name: "AnsightToolsDatabase",
            dependencies: [
                "AnsightKit",
            ],
            path: "Sources/AnsightToolsDatabase",
            linkerSettings: [
                .linkedLibrary("sqlite3"),
            ]
        ),
        .target(
            name: "AnsightToolsSecureStorage",
            dependencies: [
                "AnsightKit",
            ],
            path: "Sources/AnsightToolsSecureStorage"
        ),
        .target(
            name: "AnsightToolsVisualTree",
            dependencies: [
                "AnsightKit",
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
            name: "AnsightKitTests",
            dependencies: [
                "Ansight",
                "AnsightKit",
                "AnsightToolsDatabase",
                "AnsightToolsFileSystem",
                "AnsightToolsPreferences",
                "AnsightToolsSecureStorage",
                "AnsightToolsVisualTree",
            ],
            path: "Tests/AnsightKitTests"
        ),
    ]
)
