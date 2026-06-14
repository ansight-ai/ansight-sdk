import Foundation

public struct AnsightToolSecurity: Sendable, Codable, Equatable {
    public static let unspecified = AnsightToolSecurity(
        level: .unspecified,
        summary: "",
        implications: []
    )

    public let level: AnsightToolSecurityLevel
    public let summary: String
    public let implications: [String]

    public init(
        level: AnsightToolSecurityLevel,
        summary: String,
        implications: [String] = []
    ) {
        self.level = level
        self.summary = summary
        self.implications = implications
    }

    public var isSpecified: Bool {
        level != .unspecified ||
            !summary.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ||
            !implications.isEmpty
    }

    internal var jsonValue: JSONValue {
        var seen: Set<String> = []
        let normalizedImplications = implications.compactMap { rawValue -> String? in
            let value = rawValue.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !value.isEmpty else {
                return nil
            }

            let key = value.lowercased()
            guard !seen.contains(key) else {
                return nil
            }

            seen.insert(key)
            return value
        }

        return .object([
            "level": .string(level.rawValue),
            "summary": .string(summary),
            "implications": .array(normalizedImplications.map(JSONValue.string)),
        ])
    }
}
