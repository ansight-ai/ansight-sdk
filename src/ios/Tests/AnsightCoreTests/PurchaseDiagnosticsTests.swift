import Foundation
import XCTest
@testable import AnsightCore

final class PurchaseDiagnosticsTests: XCTestCase {
    func testEnvironmentGuardCoversEveryEntryPoint() async throws {
        let diagnostics = PurchaseDiagnostics(environmentAllowed: { false })
        XCTAssertFalse(diagnostics.isInteropAllowed)
        var row = PurchaseObservation(productId: "item", source: "app")
        row.environment = "sandbox"
        XCTAssertThrowsError(try diagnostics.record(row))
        XCTAssertThrowsError(try diagnostics.recordProduct(PurchaseProduct(productId: "item", available: true)))
        XCTAssertThrowsError(try diagnostics.createTransactionReference("token"))
        XCTAssertThrowsError(try diagnostics.recordStoreKitBatch([], products: [], generation: diagnostics.captureGeneration()))
        for action in ["record", "recordProduct", "reference", "register", "clear", "refreshStoreKit"] + PurchaseDiagnostics.toolIds {
            XCTAssertThrowsError(try diagnostics.command("{\"action\":\"\(action)\"}")) { error in
                XCTAssertEqual(error as? PurchaseDiagnosticsError, .environmentNotAllowed)
            }
            do { _ = try await diagnostics.commandAsync("{\"action\":\"\(action)\"}"); XCTFail("Allowed blocked bridge") }
            catch { XCTAssertEqual(error as? PurchaseDiagnosticsError, .environmentNotAllowed) }
        }
        for tool in diagnostics.tools() {
            XCTAssertThrowsError(try diagnostics.execute(tool.descriptor.id))
            let availability = tool.availability(context: .init(sessionId: nil, requestId: nil))
            let result = try (tool as! any AnsightJSONTool).execute(arguments: [:])
            XCTAssertEqual(result.errorCode, PurchaseDiagnosticsError.environmentErrorCode)
            XCTAssertFalse(availability.available)
            XCTAssertEqual(availability.reasonCode, PurchaseDiagnosticsError.environmentErrorCode)
        }
        if #available(macOS 12.0, iOS 15.0, *) {
            do { try await PurchaseStoreKit.refresh(productIds: [], diagnostics: diagnostics); XCTFail("Allowed blocked StoreKit") }
            catch { XCTAssertEqual(error as? PurchaseDiagnosticsError, .environmentNotAllowed) }
        }
        diagnostics.clear()
    }

    func testGuardRechecksAndRejectsProductionEvidence() throws {
        var allowed = true
        let diagnostics = PurchaseDiagnostics(environmentAllowed: { allowed })
        var row = PurchaseObservation(productId: "item", source: "store")
        row.environment = "sandbox"
        try diagnostics.record(row)
        allowed = false
        XCTAssertThrowsError(try diagnostics.execute("purchases.get_state"))
        allowed = true
        guard case .object(let result) = try diagnostics.execute("purchases.get_state") else { return XCTFail() }
        XCTAssertEqual(result["observations"], .array([]))
        row.environment = "production"
        XCTAssertThrowsError(try diagnostics.record(row))
        XCTAssertThrowsError(try diagnostics.recordStoreKitBatch([PurchaseObservation(productId: "safe", source: "store"), row], products: [], generation: diagnostics.captureGeneration()))
        #if targetEnvironment(simulator)
        XCTAssertTrue(PurchaseEnvironment.isAllowed())
        #else
        XCTAssertFalse(PurchaseEnvironment.isAllowed())
        XCTAssertFalse(PurchaseDiagnostics().isInteropAllowed)
        #endif
    }

    func testReferenceIsolationAndInflightRefreshAfterClear() throws {
        let diagnostics = PurchaseDiagnostics(environmentAllowed: { true })
        let reference = try diagnostics.createTransactionReference("raw-purchase-token")
        XCTAssertEqual(reference.count, 64)
        XCTAssertEqual(reference, try diagnostics.createTransactionReference("raw-purchase-token"))
        XCTAssertNotEqual(reference, try PurchaseDiagnostics(environmentAllowed: { true }).createTransactionReference("raw-purchase-token"))
        let generation = diagnostics.captureGeneration()
        diagnostics.clear()
        XCTAssertNotEqual(reference, try diagnostics.createTransactionReference("raw-purchase-token"))
        XCTAssertThrowsError(try diagnostics.recordStoreKitBatch([PurchaseObservation(productId: "item", source: "store")], products: [], generation: generation))
    }

    func testSharedValidationCases() throws {
        let fixtureURL = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("docs/contracts/purchases/validation-cases.json")
        let fixture = try JSONSerialization.jsonObject(with: Data(contentsOf: fixtureURL)) as! [String: Any]
        for test in fixture["cases"] as! [[String: Any]] {
            let diagnostics = PurchaseDiagnostics(environmentAllowed: { true })
            for row in test["observations"] as! [[String: Any]] {
                let request = try JSONSerialization.data(withJSONObject: ["action": "record", "observation": row])
                _ = try diagnostics.command(String(decoding: request, as: UTF8.self))
            }
            let arguments = try JSONDecoder().decode([String: JSONValue].self, from: JSONSerialization.data(withJSONObject: test["arguments"]!))
            let result = try diagnostics.execute("purchases.validate", arguments: arguments, now: 100000)
            XCTAssertTrue(AnsightToolSchemaValidator.validate(schema: PurchaseSchemas.result, value: result).isEmpty)
            guard case .object(let value) = result else { return XCTFail("Invalid report") }
            XCTAssertEqual(value["status"], .string(test["status"] as! String), test["name"] as! String)
        }
    }
    func testRetentionAndPassiveReads() throws {
        let diagnostics = PurchaseDiagnostics(environmentAllowed: { true })
        for value in 0..<300 { try diagnostics.record(PurchaseObservation(productId: "item", source: "app", observedAt: Int64(value))) }
        guard case .object(let result) = try diagnostics.execute("purchases.get_events") else { return XCTFail() }
        XCTAssertEqual(result["droppedEvents"], .integer(44))
        XCTAssertEqual(result["cursorGap"], .bool(true))
        XCTAssertEqual(result["nextCursor"], .integer(300))
        for tool in diagnostics.tools() { XCTAssertEqual(tool.descriptor.policy, .read) }
        let before = try diagnostics.execute("purchases.get_state", now: 300)
        _ = try diagnostics.execute("purchases.validate", arguments: ["productId": .string("item"), "expectedEntitled": .bool(true)], now: 300)
        XCTAssertEqual(try diagnostics.execute("purchases.get_state", now: 300), before)
        diagnostics.clear()
        XCTAssertThrowsError(try diagnostics.execute("purchases.get_events", arguments: ["after": .integer(300)]))
    }
}
