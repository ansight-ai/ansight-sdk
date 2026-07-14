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
            name: "AnsightToolsFileDescriptorDiagnostics",
            targets: ["AnsightToolsFileDescriptorDiagnostics"]
        ),
        .library(
            name: "AnsightToolsDatabase",
            targets: ["AnsightToolsDatabase"]
        ),
        .library(
            name: "AnsightToolsReflection",
            targets: ["AnsightToolsReflection"]
        ),
        .library(
            name: "AnsightToolsSecureStorage",
            targets: ["AnsightToolsSecureStorage"]
        ),
        .library(
            name: "AnsightToolsVisualTree",
            targets: ["AnsightToolsVisualTree"]
        ),
        .library(
            name: "AnsightObjC",
            targets: ["AnsightObjC"]
        ),
    ],
    targets: [
        .target(
            name: "Ansight",
            dependencies: [
                "AnsightCore",
                "AnsightPairingQR",
                "AnsightToolsDatabase",
                "AnsightToolsFileDescriptorDiagnostics",
                "AnsightToolsFileSystem",
                "AnsightToolsPreferences",
                "AnsightToolsReflection",
                "AnsightToolsSecureStorage",
                "AnsightToolsVisualTree",
            ],
            path: "Sources/Ansight",
            exclude: ["README.md"]
        ),
        .target(
            name: "AnsightCore",
            path: "Sources/AnsightCore",
            exclude: ["README.md"],
            linkerSettings: [
                .linkedLibrary("z"),
            ],
            plugins: [
                .plugin(name: "AnsightBuildToolPlugin"),
            ]
        ),
        .target(
            name: "AnsightPairingQR",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightPairingQR",
            exclude: ["README.md"]
        ),
        .target(
            name: "AnsightToolsPreferences",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsPreferences",
            exclude: ["README.md"]
        ),
        .target(
            name: "AnsightToolsFileSystem",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsFileSystem",
            exclude: ["README.md"]
        ),
        .target(
            name: "CAnsightFileDescriptorDiagnostics",
            path: "Sources/CAnsightFileDescriptorDiagnostics",
            publicHeadersPath: "include"
        ),
        .target(
            name: "AnsightToolsFileDescriptorDiagnostics",
            dependencies: [
                "AnsightCore",
                "CAnsightFileDescriptorDiagnostics",
            ],
            path: "Sources/AnsightToolsFileDescriptorDiagnostics",
            exclude: ["README.md"]
        ),
        .target(
            name: "AnsightToolsDatabase",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsDatabase",
            exclude: ["README.md"],
            linkerSettings: [
                .linkedLibrary("sqlite3"),
            ]
        ),
        .target(
            name: "AnsightToolsReflection",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsReflection",
            exclude: ["README.md"]
        ),
        .target(
            name: "AnsightToolsSecureStorage",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsSecureStorage",
            exclude: ["README.md"]
        ),
        .target(
            name: "AnsightToolsVisualTree",
            dependencies: [
                "AnsightCore",
            ],
            path: "Sources/AnsightToolsVisualTree",
            exclude: ["README.md"]
        ),
        .target(
            name: "AnsightObjC",
            dependencies: [
                "Ansight",
            ],
            path: "Sources/AnsightObjC",
            exclude: ["README.md"]
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
                "AnsightToolsFileDescriptorDiagnostics",
                "AnsightToolsFileSystem",
                "AnsightToolsPreferences",
                "AnsightToolsReflection",
                "AnsightToolsSecureStorage",
                "AnsightToolsVisualTree",
                "AnsightObjC",
            ],
            path: "Tests/AnsightCoreTests"
        ),
    ]
)
