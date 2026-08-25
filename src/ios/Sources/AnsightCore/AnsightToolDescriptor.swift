import Foundation

public struct AnsightToolDescriptor: Sendable, Codable, Equatable {
    public let id: String
    public let name: String
    public let description: String
    public let category: String
    public let policy: AnsightToolPolicy
    public let keywords: String
    public let argumentsSchema: AnsightToolSchema
    public let resultSchema: AnsightToolSchema
    public let prerequisiteToolIds: [String]

    public init(
        id: String,
        name: String,
        description: String = "",
        category: String = "Diagnostics",
        policy: AnsightToolPolicy = .read,
        keywords: String = "",
        argumentsSchema: AnsightToolSchema = .emptyObject,
        resultSchema: AnsightToolSchema = .emptyObject,
        prerequisiteToolIds: [String] = []
    ) {
        self.id = id
        self.name = name
        self.description = description
        self.category = category
        self.policy = policy
        self.keywords = keywords
        self.argumentsSchema = argumentsSchema
        self.resultSchema = resultSchema
        self.prerequisiteToolIds = prerequisiteToolIds
    }

}
