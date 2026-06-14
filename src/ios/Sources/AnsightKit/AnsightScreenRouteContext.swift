import Foundation

public struct AnsightScreenRouteContext: Sendable, Codable, Equatable {
    public var source: String
    public var defaultName: String
    public var defaultKey: String
    public var title: String?
    public var viewControllerName: String
    public var viewControllerTypeName: String
    public var swiftUIRootTypeName: String?
    public var details: [String: String]

    public init(
        source: String,
        defaultName: String,
        defaultKey: String,
        title: String? = nil,
        viewControllerName: String,
        viewControllerTypeName: String,
        swiftUIRootTypeName: String? = nil,
        details: [String: String] = [:]
    ) {
        self.source = source
        self.defaultName = defaultName
        self.defaultKey = defaultKey
        self.title = title
        self.viewControllerName = viewControllerName
        self.viewControllerTypeName = viewControllerTypeName
        self.swiftUIRootTypeName = swiftUIRootTypeName
        self.details = details
    }
}
