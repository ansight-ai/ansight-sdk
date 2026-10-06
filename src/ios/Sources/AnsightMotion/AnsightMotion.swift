@_exported import AnsightCore

/// Forwards motion already observed by the app into the opt-in core capture stream.
public enum AnsightMotion {
    public static func recordShake(source: String = "uikit") {
        AnsightRuntime.shared.recordShake(source: source)
    }

    /// Core Motion reports acceleration in g. Convert to m/s² before forwarding.
    public static func recordAccelerometer(x: Double, y: Double, z: Double) {
        AnsightRuntime.shared.recordAccelerometerSample(x: x, y: y, z: z)
    }
}
