import Foundation

public struct AnsightToolDescriptor: Sendable, Codable, Equatable {
    public let id: String
    public let name: String
    public let description: String
    public let category: String
    public let scope: String
    public let keywords: String
    public let security: AnsightToolSecurity
    public let argumentsSchema: AnsightToolSchema
    public let resultSchema: AnsightToolSchema

    public init(
        id: String,
        name: String,
        description: String = "",
        category: String = "Diagnostics",
        scope: String = AnsightToolScope.read.rawValue,
        keywords: String = "",
        security: AnsightToolSecurity = .unspecified,
        argumentsSchema: AnsightToolSchema = .emptyObject,
        resultSchema: AnsightToolSchema = .emptyObject
    ) {
        self.id = id
        self.name = name
        self.description = description
        self.category = category
        self.scope = scope
        self.keywords = keywords
        self.security = security
        self.argumentsSchema = argumentsSchema
        self.resultSchema = resultSchema
    }

    public var scopeValue: AnsightToolScope? {
        AnsightToolScope(rawValue: scope)
    }
}
