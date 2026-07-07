struct HarnessReflectionRootDescriptor {
    let id: String
    let name: String
    let kind: String
    let description: String
    let hostRuntime: HarnessReflectionHostRuntimeDescriptor
}

struct HarnessReflectionHostRuntimeDescriptor {
    let kind: String
    let displayName: String
    let platform: String
    let engine: String

    static let nativeSwift = HarnessReflectionHostRuntimeDescriptor(
        kind: "swift",
        displayName: "Swift/Objective-C runtime",
        platform: "ios",
        engine: "Swift"
    )
}
