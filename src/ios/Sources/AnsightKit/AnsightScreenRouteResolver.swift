import Foundation

public struct AnsightScreenRouteResolver: Sendable {
    private let handler: @Sendable (AnsightScreenRouteContext) -> AnsightScreenRoute?

    public init(_ handler: @escaping @Sendable (AnsightScreenRouteContext) -> AnsightScreenRoute?) {
        self.handler = handler
    }

    public func resolve(_ context: AnsightScreenRouteContext) -> AnsightScreenRoute? {
        handler(context)
    }
}
