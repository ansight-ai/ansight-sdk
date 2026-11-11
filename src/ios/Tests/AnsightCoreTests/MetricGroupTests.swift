import XCTest
@testable import AnsightCore

final class MetricGroupTests: XCTestCase {
    func testTypedGroupSurvivesValidationWireAndOfflineEncoding() throws {
        let group = AnsightMetricGroup(id: " checkout ", name: " Checkout ")
        let channel = try AnsightRuntime.validatedMetricChannel(
            AnsightChannel(id: 64, name: "Payment latency", unit: "ms", group: "Old label", metricGroup: group),
            allowReservedIds: false, allowUnspecified: false)
        XCTAssertEqual(channel.group, "Checkout")
        XCTAssertEqual(channel.metricGroup?.id, "checkout")
        let decoded = try JSONDecoder().decode(AnsightChannel.self, from: JSONEncoder().encode(channel))
        XCTAssertEqual(decoded, channel)

        let wire = try JSONSerialization.jsonObject(with: JSONEncoder().encode(channel.jsonValue)) as! [String: Any]
        XCTAssertEqual((wire["metricGroup"] as? [String: String])?["id"], "checkout")
        XCTAssertEqual(wire["group"] as? String, "Checkout")
        let offline = AnsightRuntime.makeMetricsPayload([RecordedMetric(value: 16.67, channel: 64)], channels: [channel])
        let fields = try JSONSerialization.jsonObject(with: JSONEncoder().encode(offline)) as! [String: Any]
        let retainedChannel = try XCTUnwrap((fields["channels"] as? [[String: Any]])?.first)
        XCTAssertEqual((retainedChannel["metricGroup"] as? [String: String])?["id"], "checkout")
    }

    func testLegacyChannelsAndInvalidGroups() throws {
        let channel = try JSONDecoder().decode(AnsightChannel.self,
            from: Data(#"{"id":64,"name":"Payment latency","group":"Checkout"}"#.utf8))
        XCTAssertNil(channel.metricGroup)
        XCTAssertEqual(channel.group, "Checkout")
        XCTAssertThrowsError(try JSONDecoder().decode(AnsightMetricGroup.self,
            from: Data(#"{"id":"checkout","name":" "}"#.utf8)))
        XCTAssertThrowsError(try AnsightRuntime.validatedMetricChannel(
            AnsightChannel(id: 64, name: "Test", metricGroup: AnsightMetricGroup(id: " ", name: "Test")),
            allowReservedIds: false, allowUnspecified: false))
    }

    func testBuiltInChannelsDeclareSharedGroups() {
        XCTAssertEqual(AnsightChannels.physicalFootprintChannel.metricGroup, AnsightMetricGroups.memory)
        XCTAssertEqual(AnsightChannels.framesPerSecondChannel.metricGroup, AnsightMetricGroups.rendering)
        XCTAssertEqual(AnsightChannels.openFileHandlesChannel.metricGroup, AnsightMetricGroups.runtime)
        XCTAssertEqual(AnsightChannels.batteryLevelChannel.metricGroup, AnsightMetricGroups.device)
    }
}
