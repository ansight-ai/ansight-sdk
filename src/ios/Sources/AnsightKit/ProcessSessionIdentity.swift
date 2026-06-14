import Foundation
import Network

public enum ProcessSessionIdentity {
    public static let current = "ios-\(UUID().uuidString)"
}
