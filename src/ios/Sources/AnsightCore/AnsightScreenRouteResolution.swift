import Foundation

enum AnsightScreenRouteResolution {
    static func resolve(
        defaultDescriptor: AnsightScreenDescriptor,
        context: AnsightScreenRouteContext,
        resolver: AnsightScreenRouteResolver?
    ) -> AnsightScreenDescriptor {
        guard let route = resolver?.resolve(context),
              let name = normalized(route.name)
        else {
            return defaultDescriptor
        }

        var details = defaultDescriptor.details
        for (key, value) in route.details {
            guard let normalizedKey = normalized(key),
                  let normalizedValue = normalized(value)
            else {
                continue
            }

            details[normalizedKey] = normalizedValue
        }

        return AnsightScreenDescriptor(
            name: name,
            key: normalized(route.key) ?? "custom:\(name)",
            details: details
        )
    }

    private static func normalized(_ value: String?) -> String? {
        let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return trimmed.isEmpty ? nil : trimmed
    }
}
