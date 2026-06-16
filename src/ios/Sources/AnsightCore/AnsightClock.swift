import Foundation

public enum AnsightClock {
    public static func isoNow() -> String {
        isoString(from: Date())
    }

    public static func isoString(from date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }

    public static func epochMilliseconds(fromISO8601 value: String) -> Int64 {
        let date = parseISO8601(value) ?? Date()
        return Int64(date.timeIntervalSince1970 * 1_000)
    }

    public static func parseISO8601(_ value: String) -> Date? {
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let parsed = fractional.date(from: value) {
            return parsed
        }

        let standard = ISO8601DateFormatter()
        standard.formatOptions = [.withInternetDateTime]
        return standard.date(from: value)
    }
}
