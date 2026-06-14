import Foundation

public enum AnsightChannels {
    public static let managedHeap = 0
    public static let physicalFootprint = 1
    public static let framesPerSecond = 3
    public static let lifecycle = 4
    public static let batteryLevel = 5
    public static let unspecified = 255

    public static let reservedIds: Set<Int> = [
        managedHeap,
        physicalFootprint,
        framesPerSecond,
        lifecycle,
        batteryLevel,
        unspecified,
    ]

    public static let managedHeapChannel = AnsightChannel(id: managedHeap, name: ".NET", color: "#5C2D90")
    public static let physicalFootprintChannel = AnsightChannel(id: physicalFootprint, name: "Physical Footprint", color: "#007AFF")
    public static let framesPerSecondChannel = AnsightChannel(id: framesPerSecond, name: "FPS", color: "#23B573")
    public static let lifecycleChannel = AnsightChannel(id: lifecycle, name: "Lifecycle", color: "#FF9500")
    public static let batteryLevelChannel = AnsightChannel(id: batteryLevel, name: "Battery Level", color: "#FFCC00")
    public static let unspecifiedChannel = AnsightChannel(id: unspecified, name: "Not Specified", color: nil)
}
