import Foundation

public enum AnsightSamplingLimits {
    public static let defaultSampleFrequencyMilliseconds = 500
    public static let minSampleFrequencyMilliseconds = 200
    public static let maxSampleFrequencyMilliseconds = 2_000
    public static let defaultRetentionPeriodSeconds = 600
    public static let minRetentionPeriodSeconds = 60
    public static let maxRetentionPeriodSeconds = 3_600
}
