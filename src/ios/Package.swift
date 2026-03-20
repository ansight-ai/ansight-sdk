// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AnsightKit",
    platforms: [
        .iOS(.v15),
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
            path: "Sources/AnsightKit"
        ),
    ]
)
