import XCTest
@testable import AnsightCore

func decodedToolProtocolPayload(
    _ envelope: AnsightToolProtocolEnvelope
) throws -> [String: JSONValue] {
    let decodedPayload = try XCTUnwrap(
        AnsightToolProtocolPayloadEncoding.decodeIfNeeded(envelope.payload)
    )
    guard case .object(let payload) = decodedPayload else {
        XCTFail("Expected tool protocol object payload.")
        return [:]
    }

    return payload
}
