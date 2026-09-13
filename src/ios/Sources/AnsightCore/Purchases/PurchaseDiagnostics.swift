import Foundation
import CryptoKit

/// Sanitized evidence supplied by a store observer, the app, or its backend.
public struct PurchaseObservation: Sendable, Codable, Equatable {
    public let productId: String
    public let source: String
    public let observedAt: Int64
    public var state: String = "unknown"
    public var verification: String = "unknown"
    public var entitled: Bool?
    public var deliveryCount: Int?
    public var finished: Bool?
    public var acknowledged: Bool?
    public var consumed: Bool?
    public var expiresAt: Int64?
    public var gracePeriodExpiresAt: Int64?
    public var environment: String = "unknown"
    public var productType: String = "unknown"
    public var transactionRef: String?

    public init(productId: String, source: String, observedAt: Int64 = PurchaseDiagnostics.now()) {
        self.productId = productId; self.source = source; self.observedAt = observedAt
    }
}

/// Passive bounded evidence. Clearing diagnostics does not alter store purchases.
public final class PurchaseDiagnostics: @unchecked Sendable {
    public static let shared = PurchaseDiagnostics()
    public static let toolIds = ["purchases.query_products", "purchases.get_state", "purchases.query_transactions", "purchases.get_entitlements", "purchases.get_events", "purchases.validate"]
    private let lock = NSRecursiveLock()
    private var generation: UInt64 = 0
    private var referenceSalt = UUID().uuidString
    private var observations: [PurchaseObservation] = []
    private var dropped: Int64 = 0
    private var products: [PurchaseProduct] = []
    private let environmentAllowed: () -> Bool
    public init() { environmentAllowed = PurchaseEnvironment.isAllowed }
    internal init(environmentAllowed: @escaping () -> Bool) { self.environmentAllowed = environmentAllowed }
    public var isInteropAllowed: Bool { environmentAllowed() }
    public func ensureInteropAllowed() throws {
        guard isInteropAllowed else { clear(); throw PurchaseDiagnosticsError.environmentNotAllowed }
    }
    public static func now() -> Int64 { Int64(Date().timeIntervalSince1970 * 1000) }

    public func createTransactionReference(_ identifier: String) throws -> String {
        try ensureInteropAllowed()
        guard !identifier.isEmpty, identifier.count <= 8192 else { throw PurchaseDiagnosticsError.invalidArguments }
        lock.lock(); defer { lock.unlock() }
        return SHA256.hash(data: Data((referenceSalt + identifier).utf8)).map { String(format: "%02x", $0) }.joined()
    }

    public func record(_ value: PurchaseObservation) throws {
        try ensureInteropAllowed()
        guard value.environment != "production" else { throw PurchaseDiagnosticsError.environmentNotAllowed }
        guard !value.productId.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, value.productId.count <= 256,
              ["store", "app", "backend"].contains(value.source),
              ["unknown", "pending", "purchased", "cancelled", "expired", "revoked", "failed", "gracePeriod", "billingRetry"].contains(value.state),
              ["unknown", "verified", "unverified"].contains(value.verification),
              ["unknown", "xcode", "sandbox", "production"].contains(value.environment),
              ["unknown", "consumable", "nonConsumable", "subscription", "nonRenewingSubscription"].contains(value.productType),
              (value.transactionRef == nil || (value.transactionRef!.count == 64 && value.transactionRef!.allSatisfy { "0123456789abcdef".contains($0) })),
              value.observedAt >= 0, (value.expiresAt ?? 0) >= 0, (value.gracePeriodExpiresAt ?? 0) >= 0, (value.deliveryCount ?? 0) >= 0
        else { throw PurchaseDiagnosticsError.invalidArguments }
        lock.lock(); defer { lock.unlock() }
        if observations.count == 256 { observations.removeFirst(); dropped += 1 }
        observations.append(value)
    }

    internal func captureGeneration() -> UInt64 {
        lock.lock(); defer { lock.unlock() }; return generation
    }
    internal func recordStoreKitBatch(_ rows: [PurchaseObservation], products: [PurchaseProduct], generation expected: UInt64) throws {
        lock.lock(); defer { lock.unlock() }
        try ensureInteropAllowed()
        guard rows.allSatisfy({ $0.environment != "production" }) else { throw PurchaseDiagnosticsError.environmentNotAllowed }
        guard generation == expected else { throw CancellationError() }
        for row in rows { try record(row) }
        for product in products { try recordProduct(product) }
    }

    public func recordProduct(_ value: PurchaseProduct) throws {
        try ensureInteropAllowed()
        guard !value.productId.isEmpty, value.productId.count <= 256, value.observedAt >= 0, ["unknown", "consumable", "nonConsumable", "subscription", "nonRenewingSubscription"].contains(value.productType), [value.displayPrice, value.price, value.currencyCode].allSatisfy({ ($0?.count ?? 0) <= 256 }) else { throw PurchaseDiagnosticsError.invalidArguments }
        lock.lock(); defer { lock.unlock() }
        products.removeAll { $0.productId == value.productId }
        if products.count == 256 { products.removeFirst() }
        products.append(value)
    }

    public func clear() { lock.lock(); defer { lock.unlock() }; observations.removeAll(); products.removeAll(); dropped = 0; generation &+= 1; referenceSalt = UUID().uuidString }

    public func execute(_ toolId: String, arguments: [String: JSONValue] = [:], now: Int64 = PurchaseDiagnostics.now()) throws -> JSONValue {
        try ensureInteropAllowed()
        guard Self.toolIds.contains(toolId) else { throw PurchaseDiagnosticsError.invalidArguments }
        let allowed = toolId == "purchases.validate" ? ["productId", "transactionRef", "expectedEntitled", "requireVerified", "maxAgeMilliseconds", "requireBackendVerification", "expectedDeliveryCount"] : toolId == "purchases.get_events" ? ["after"] : ["productId", "transactionRef"]
        guard arguments.keys.allSatisfy(allowed.contains) else { throw PurchaseDiagnosticsError.invalidArguments }
        let product: String?
        if let value = arguments["productId"] {
            guard case .string(let id) = value, !id.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, id.count <= 256 else { throw PurchaseDiagnosticsError.invalidArguments }
            product = id
        } else { product = nil }
        let reference: String?
        if let raw = arguments["transactionRef"] {
            guard case .string(let value) = raw, value.count == 64, value.allSatisfy({ "0123456789abcdef".contains($0) }) else { throw PurchaseDiagnosticsError.invalidArguments }; reference = value
        } else { reference = nil }
        lock.lock(); defer { lock.unlock() }
        let rows = observations.filter { (product == nil || $0.productId == product) && (reference == nil || $0.transactionRef == reference) }
        var result: [String: JSONValue] = ["schema": .string("ansight.purchases.v1"), "capturedAt": .integer(now), "droppedEvents": .integer(dropped), "coverage": .string("observedOnly"), "observations": try .fromEncodable(rows)]
        result["products"] = try .fromEncodable(products.filter { product == nil || $0.productId == product })
        if toolId == "purchases.get_events" {
            let after = try integer(arguments["after"], default: 0)
            guard after >= 0, after <= dropped + Int64(observations.count) else { throw PurchaseDiagnosticsError.invalidArguments }
            result["observations"] = try .fromEncodable(Array(observations.dropFirst(Int(max(0, after - dropped)))))
            result["nextCursor"] = .integer(dropped + Int64(observations.count))
            result["cursorGap"] = .bool(after < dropped)
        }
        if toolId == "purchases.validate" {
            guard product != nil, case .bool(let expected) = arguments["expectedEntitled"] else { throw PurchaseDiagnosticsError.invalidArguments }
            let verify: Bool
            if let raw = arguments["requireVerified"] { guard case .bool(let value) = raw else { throw PurchaseDiagnosticsError.invalidArguments }; verify = value } else { verify = true }
            let maxAge = try integer(arguments["maxAgeMilliseconds"], default: 60_000)
            guard maxAge >= 1, maxAge <= 86_400_000 else { throw PurchaseDiagnosticsError.invalidArguments }
            let backend: Bool
            if let raw = arguments["requireBackendVerification"] { guard case .bool(let value) = raw else { throw PurchaseDiagnosticsError.invalidArguments }; backend = value } else { backend = false }
            let delivery = try arguments["expectedDeliveryCount"].map { try integer($0, default: 0) }
            guard (delivery ?? 0) >= 0 else { throw PurchaseDiagnosticsError.invalidArguments }
            var checks: [JSONValue] = []; var statuses: [String] = []
            for source in (backend ? ["store", "app", "backend"] : ["store", "app"]) {
                let observation = rows.reversed().filter { $0.source == source }.max { $0.observedAt < $1.observedAt }
                var status = "inconclusive"; var reason = "missing_evidence"
                if let observation {
                    let age = now - observation.observedAt
                    if age < 0 || age > maxAge { reason = "stale_evidence" }
                    else if let entitled = observation.entitled ?? (source == "store" && observation.productType == "consumable" ? observation.state == "purchased" : nil) {
                        let effectiveExpiry = observation.state == "gracePeriod" ? observation.gracePeriodExpiresAt : observation.expiresAt
                        let active = entitled && (source != "store" || ((effectiveExpiry == nil || effectiveExpiry! > now) && !["revoked", "expired", "pending", "cancelled", "failed", "billingRetry"].contains(observation.state)))
                        status = active == expected ? "pass" : "fail"
                        reason = active == expected ? "entitlement_matches" : "entitlement_mismatch"
                        if source == "store" && observation.state == "gracePeriod" && effectiveExpiry == nil && status == "pass" && expected { status = "inconclusive"; reason = "missing_grace_period_expiry" }
                        if expected && source == "store" && !["purchased", "gracePeriod"].contains(observation.state) { status = "fail"; reason = "store_not_purchased" }
                        if status != "fail" && ((expected && source == "store" && verify && !backend) || (source == "backend" && backend)) && observation.verification != "verified" {
                            status = observation.verification == "unverified" ? "fail" : "inconclusive"; reason = "verification_" + observation.verification
                        }
                        if source == "app", let delivery, status != "fail", observation.deliveryCount.map(Int64.init) != delivery {
                            status = observation.deliveryCount == nil ? "inconclusive" : "fail"
                            reason = observation.deliveryCount == nil ? "missing_delivery_count" : "delivery_count_mismatch"
                        }
                    } else { reason = "unknown_entitlement" }
                }
                if let observation, now >= observation.observedAt, now - observation.observedAt <= maxAge,
                   observation.verification == "unverified",
                   ((expected && source == "store" && verify && !backend) || (source == "backend" && backend)) {
                    status = "fail"; reason = "verification_unverified"
                }
                statuses.append(status)
                checks.append(.object(["source": .string(source), "status": .string(status), "reason": .string(reason)]))
            }
            result["status"] = .string(statuses.contains("fail") ? "fail" : statuses.contains("inconclusive") ? "inconclusive" : "pass")
            result["checks"] = .array(checks)
        }
        return .object(result)
    }

    private func integer(_ value: JSONValue?, default fallback: Int64) throws -> Int64 {
        guard let value else { return fallback }
        guard case .integer(let number) = value else { throw PurchaseDiagnosticsError.invalidArguments }
        return number
    }

    /// Local app bridge, not a remotely registered mutation tool.
    public func command(_ json: String) throws -> String {
        try ensureInteropAllowed()
        guard let data = json.data(using: .utf8), case .object(let request) = try JSONDecoder().decode(JSONValue.self, from: data), case .string(let action) = request["action"] else { throw PurchaseDiagnosticsError.invalidArguments }
        if action == "reference" {
            guard case .string(let identifier) = request["identifier"] else { throw PurchaseDiagnosticsError.invalidArguments }
            return try JSONValue.object(["transactionRef": .string(createTransactionReference(identifier))]).jsonString()
        }
        if action == "record" {
            guard case .object(var value) = request["observation"] else { throw PurchaseDiagnosticsError.invalidArguments }
            for key in ["state", "verification", "environment", "productType"] where value[key] == nil { value[key] = .string("unknown") }
            if value["observedAt"] == nil { value["observedAt"] = .integer(Self.now()) }
            let observation = try JSONDecoder().decode(PurchaseObservation.self, from: JSONValue.object(value).jsonData())
            try record(observation)
        } else if action == "recordProduct" {
            guard let value = request["product"] else { throw PurchaseDiagnosticsError.invalidArguments }
            try recordProduct(JSONDecoder().decode(PurchaseProduct.self, from: value.jsonData()))
        } else if action == "clear" { clear() }
        else if action == "register" { for tool in tools() { try AnsightRuntime.shared.registerTool(tool, replaceExisting: true) } }
        else {
            let args: [String: JSONValue]
            if case .object(let value) = request["arguments"] { args = value } else { args = [:] }
            return try execute(action, arguments: args).jsonString()
        }
        return "{\"success\":true}"
    }

    public func commandAsync(_ json: String) async throws -> String {
        try ensureInteropAllowed()
        let request = try JSONDecoder().decode([String: JSONValue].self, from: Data(json.utf8))
        if request["action"] == .string("refreshStoreKit") {
            guard case .array(let values) = request["productIds"] else { throw PurchaseDiagnosticsError.invalidArguments }
            let ids = try values.map { value -> String in
                guard case .string(let id) = value else { throw PurchaseDiagnosticsError.invalidArguments }; return id
            }
            #if canImport(StoreKit)
            if #available(iOS 15.0, macOS 12.0, *) {
                try await PurchaseStoreKit.refresh(productIds: ids, diagnostics: self)
                return try execute("purchases.get_state").jsonString()
            }
            #endif
            throw PurchaseDiagnosticsError.invalidArguments
        }
        return try command(json)
    }

    public func tools() -> [any AnsightTool] { Self.toolIds.map { PurchaseTool(id: $0, diagnostics: self) } }
}

public enum PurchaseDiagnosticsError: Error, Equatable, LocalizedError {
    case invalidArguments, environmentNotAllowed
    public var errorDescription: String? {
        self == .environmentNotAllowed ? "purchases_environment_not_allowed: Purchase interop is restricted to simulators and emulators." : "Invalid purchase arguments."
    }
    public static let environmentErrorCode = "purchases_environment_not_allowed"
}

private struct PurchaseTool: AnsightJSONTool {
    let id: String
    let diagnostics: PurchaseDiagnostics
    var descriptor: AnsightToolDescriptor {
        let properties: [String: JSONValue] = id == "purchases.validate"
            ? ["transactionRef": .object(["type": .string("string")]), "productId": .object(["type": .string("string")]), "expectedEntitled": .object(["type": .string("boolean")]), "requireVerified": .object(["type": .string("boolean")]), "requireBackendVerification": .object(["type": .string("boolean")]), "expectedDeliveryCount": .object(["type": .string("integer")]), "maxAgeMilliseconds": .object(["type": .string("integer")])]
            : id == "purchases.get_events" ? ["after": .object(["type": .string("integer")])] : ["transactionRef": .object(["type": .string("string")]), "productId": .object(["type": .string("string")])]
        return AnsightToolDescriptor(id: id, name: id, description: "Inspect passive purchase evidence. Missing evidence is inconclusive; never grants or finishes purchases.", category: "purchases", policy: .read, keywords: "storekit billing entitlement validation", argumentsSchema: AnsightToolSchema(json: .object(["type": .string("object"), "properties": .object(properties), "additionalProperties": .bool(false), "required": .array(id == "purchases.validate" ? [.string("productId"), .string("expectedEntitled")] : [])])), resultSchema: PurchaseSchemas.result)
    }
    func availability(context: AnsightToolAvailabilityContext) -> AnsightToolAvailability {
        diagnostics.isInteropAllowed ? .availableNow : .unavailable(reasonCode: PurchaseDiagnosticsError.environmentErrorCode,
            reason: "Purchase interop is restricted to simulators and emulators.", requiredState: "simulator")
    }
    func execute(arguments: [String: JSONValue]) throws -> AnsightToolExecutionResult {
        do { return .success(try diagnostics.execute(id, arguments: arguments)) }
        catch PurchaseDiagnosticsError.environmentNotAllowed { return .failure("Purchase interop is restricted to simulators and emulators.", errorCode: PurchaseDiagnosticsError.environmentErrorCode) }
        catch { return .failure("Invalid purchase arguments.", errorCode: "purchases_invalid_arguments") }
    }
}

public extension AnsightRuntime {
    func registerPurchaseTools(diagnostics: PurchaseDiagnostics = .shared) throws {
        for tool in diagnostics.tools() { try registerTool(tool, replaceExisting: true) }
    }
}
