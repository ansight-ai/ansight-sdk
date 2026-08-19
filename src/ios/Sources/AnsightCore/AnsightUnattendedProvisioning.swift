import Foundation

#if canImport(Darwin)
import Darwin
#endif

public enum AnsightUnattendedProvisioning {
    public static let payloadEnvironmentVariableName = "ANSIGHT_ENROLLMENT_PAYLOAD"

    static func payload(
        enabled: Bool,
        environment: [String: String] = ProcessInfo.processInfo.environment
    ) -> String? {
        guard enabled else {
            return nil
        }

        return environment[payloadEnvironmentVariableName]?
            .trimmingCharacters(in: .whitespacesAndNewlines)
            .nonEmpty
    }

    static func clearPayloadFromEnvironment() {
        #if canImport(Darwin)
        unsetenv(payloadEnvironmentVariableName)
        #endif
    }
}

private extension String {
    var nonEmpty: String? {
        isEmpty ? nil : self
    }
}
