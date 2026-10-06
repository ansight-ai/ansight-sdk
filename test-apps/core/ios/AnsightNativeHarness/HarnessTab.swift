import Foundation

enum HarnessTab: String, CaseIterable, Identifiable {
    case dashboard
    case viewer3D
    case navigation
    case data
    case snapshot

    var id: String { rawValue }

    var screenName: String {
        switch self {
        case .dashboard:
            return "Harness Dashboard"
        case .viewer3D:
            return "Inline 3D Viewer"
        case .navigation:
            return "Navigation Playground"
        case .data:
            return "Data Inspector"
        case .snapshot:
            return "Runtime Snapshot"
        }
    }
}
