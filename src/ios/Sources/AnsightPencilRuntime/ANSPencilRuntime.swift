import Foundation

/// Native implementation of the optional Pencil package. No dependency on the Ansight runtime.
@objc(ANSPencilRuntime)
public final class ANSPencilRuntime: NSObject {
    @objc public static var isSupported: Bool {
#if targetEnvironment(simulator) && canImport(UIKit)
        true
#else
        false
#endif
    }

    /// Call through the package's registered write-policy tool. Merely linking this framework installs no swizzles.
    @objc(executeWithArgumentsJson:)
    public static func execute(argumentsJson: String) -> String {
        let result: PencilExecutionResult
#if targetEnvironment(simulator) && canImport(UIKit)
        do {
            guard argumentsJson.utf8.count <= 1_048_576 else { throw PenError.invalid }
            let command = try JSONDecoder().decode(PencilCommand.self, from: Data(argumentsJson.utf8))
            guard UUID(uuidString: command.runId) != nil else { throw PenError.invalid }
            if command.action == "prepare" {
                guard let samples = command.samples, (2...2000).contains(samples.count) else { throw PenError.invalid }
            }
            let operation = { @MainActor () throws -> PencilExecutionResult in
                switch command.action {
                case "prepare": try PenOverrides.prepare(command.runId, values: command.samples ?? [], target: command.target, hoverName: command.hoverRecognizer)
                case "arm":
                    guard PenOverrides.runId == command.runId, let sample = command.sample else { throw PenError.invalid }
                    try PenOverrides.arm(sample)
                case "finish": if PenOverrides.runId == command.runId { PenOverrides.finish() }
                case "hover":
                    guard PenOverrides.runId == command.runId, let sample = command.sample else { throw PenError.invalid }
                    let count = try PenOverrides.hover(sample)
                    return .success(recognizerCount: count)
                default: throw PenError.invalid
                }
                return .success()
            }
            if Thread.isMainThread { result = try MainActor.assumeIsolated { try operation() } }
            else { result = try DispatchQueue.main.sync { try MainActor.assumeIsolated { try operation() } } }
        } catch let error as PenError {
            result = .failure(error.message, code: error.code)
        } catch {
            result = .failure("Invalid pen simulation arguments or no active window.", code: "pen_invalid_arguments")
        }
#else
        result = .failure("Pen simulation is available only in iOS Simulator.", code: "pen_unavailable")
#endif
        // These fixed, finite result types always encode successfully.
        return String(decoding: try! JSONEncoder().encode(result), as: UTF8.self)
    }
}

private struct PencilExecutionResult: Encodable, Sendable {
    let success: Bool
    let message: String?
    let errorCode: String?
    let result: PencilExecutionPayload?

    static func success(recognizerCount: Int? = nil) -> Self {
        Self(success: true, message: nil, errorCode: nil,
             result: PencilExecutionPayload(performed: true, recognizerCount: recognizerCount))
    }
    static func failure(_ message: String, code: String) -> Self {
        Self(success: false, message: message, errorCode: code, result: nil)
    }
}
private struct PencilExecutionPayload: Encodable, Sendable {
    let protocolVersion = 2
    let performed: Bool
    let recognizerCount: Int?
}

#if targetEnvironment(simulator) && canImport(UIKit)
import UIKit
import ObjectiveC.runtime

private struct PencilCommand: Decodable, Sendable {
    let action: String
    let runId: String
    let samples: [PenSample]?
    let sample: PenSample?
    let target: String?
    let hoverRecognizer: String?
}
private struct PenSample: Decodable, Sendable {
    let phase: String
    let x: Double
    let y: Double
    let pressure: Double
    let altitude: Double
    let azimuth: Double
    let roll: Double
    let distance: Double
    let eventTimestamp: Double?

    private enum CodingKeys: String, CodingKey {
        case phase, x, y, pressure, altitudeRadians, azimuthRadians, rollRadians, distance, eventTimestamp
    }
    init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        phase = try values.decode(String.self, forKey: .phase)
        x = try values.decode(Double.self, forKey: .x)
        y = try values.decode(Double.self, forKey: .y)
        pressure = try values.decodeIfPresent(Double.self, forKey: .pressure) ?? 0
        altitude = try values.decodeIfPresent(Double.self, forKey: .altitudeRadians) ?? .pi / 2
        azimuth = try values.decodeIfPresent(Double.self, forKey: .azimuthRadians) ?? 0
        roll = try values.decodeIfPresent(Double.self, forKey: .rollRadians) ?? 0
        distance = try values.decodeIfPresent(Double.self, forKey: .distance) ?? 0
        eventTimestamp = try values.decodeIfPresent(Double.self, forKey: .eventTimestamp)
        guard ["down", "move", "up", "cancel", "hoverEnter", "hoverMove", "hoverExit"].contains(phase),
              [x, y, pressure, altitude, azimuth, roll, distance].allSatisfy(\.isFinite),
              (0...1).contains(x), (0...1).contains(y), (0...1).contains(pressure),
              (0...1).contains(distance), (0...Double.pi / 2).contains(altitude),
              ["down", "move"].contains(phase) || pressure == 0 else { throw PenError.invalid }
    }
}
private enum PenError: Error {
    case invalid, busy, unavailable, target, hover, conflict, timestamp
    var code: String {
        switch self {
        case .invalid: "pen_invalid_arguments"
        case .busy: "pen_busy"
        case .unavailable: "pen_unavailable"
        case .target: "pen_target_unavailable"
        case .hover: "pen_hover_unavailable"
        case .conflict: "pen_hook_conflict"
        case .timestamp: "pen_timestamp_invalid"
        }
    }
    var message: String {
        switch self {
        case .invalid: "Invalid pen simulation arguments."
        case .busy: "A pen simulation is already active."
        case .unavailable: "Pen simulation is unavailable."
        case .target: "Specify a unique visible target accessibility identifier. Every sample must hit that drawing view; its window and geometry must remain unchanged."
        case .hover: "Hover requires one named, enabled, idle Pencil recognizer directly on the target view. Its attachment, configuration and state must remain unchanged during playback."
        case .conflict: "Another implementation intercepts a required UIKit method. Pencil playback stopped without replacing it."
        case .timestamp: "Contact samples require a fresh, increasing HID timestamp supplied by the matching host."
        }
    }
}

@MainActor
private enum PenOverrides {
    static var runId: String?
    static var expiresAt: TimeInterval = 0
    static var samples: [PenSample] = []
    static var nextSample = 0
    static var armed: [Int64: PenSample] = [:]
    static var lastTimestamp: Double = 0
    // A timestamp never transfers a cached binding between different UITouch objects.
    static var bindings = NSMapTable<UITouch, PenTouchBinding>.weakToStrongObjects()
    static weak var targetView: UIView?
    static weak var targetWindow: UIWindow?
    static var targetFrame = CGRect.zero
    static var windowBounds = CGRect.zero
    static weak var hoverRecognizer: UIHoverGestureRecognizer?
    static var hoverName: String?
    static var hoverBinding: PenHoverBinding?
    static var ownedHoverState: UIGestureRecognizer.State?
    static var terminalHoverState: UIGestureRecognizer.State?
    static var hooks: [PenHook] = []

    static var active: Bool { runId != nil && ProcessInfo.processInfo.systemUptime < expiresAt }
    static var hooksIntact: Bool { hooks.allSatisfy { $0.isIntact } }

    static func prepare(_ id: String, values: [PenSample], target: String?, hoverName: String?) throws {
        guard !active else { throw PenError.busy }
        finish()
        guard let target, !target.isEmpty else { throw PenError.target }
        let windows = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
            .filter { $0.activationState == .foregroundActive }.flatMap(\.windows).filter { $0.isKeyWindow && !$0.isHidden }
        var candidates: [UIView] = []
        func find(_ view: UIView) {
            guard !view.isHidden, view.alpha > 0.01, view.isUserInteractionEnabled else { return }
            if view.accessibilityIdentifier == target { candidates.append(view) }
            view.subviews.forEach(find)
        }
        windows.forEach(find)
        guard candidates.count == 1, let view = candidates.first, !(view is UIWindow),
              let window = view.window, window.bounds.width > 0, window.bounds.height > 0,
              values.allSatisfy({ hits($0, view: view, window: window) }) else { throw PenError.target }
        let needsHover = values.contains { $0.phase == "hoverEnter" || $0.phase == "hoverMove" }
        var selected: UIHoverGestureRecognizer?
        if let hoverName {
            let matches = (view.gestureRecognizers ?? []).compactMap { $0 as? UIHoverGestureRecognizer }.filter { $0.name == hoverName }
            guard matches.count == 1, let recognizer = matches.first,
                  recognizer.isEnabled, recognizer.state == .possible,
                  recognizer.allowedTouchTypes.contains(NSNumber(value: UITouch.TouchType.pencil.rawValue)) else { throw PenError.hover }
            selected = recognizer
        } else if needsHover { throw PenError.hover }
        // Preflight every method before mutating any of them. No partial installation on conflict.
        try install(includeHover: selected != nil)
        targetView = view; targetWindow = window
        targetFrame = view.convert(view.bounds, to: window); windowBounds = window.bounds
        hoverRecognizer = selected; self.hoverName = hoverName
        runId = id; expiresAt = ProcessInfo.processInfo.systemUptime + 35
        samples = values.filter { !$0.phase.hasPrefix("hover") }
        Task { @MainActor in
            try? await Task.sleep(nanoseconds: 35_000_000_000)
            if runId == id { finish() }
        }
    }

    static func hits(_ sample: PenSample, view: UIView, window: UIWindow) -> Bool {
        let point = CGPoint(x: sample.x * window.bounds.width, y: sample.y * window.bounds.height)
        guard let hit = window.hitTest(point, with: nil) else { return false }
        return hit === view || hit.isDescendant(of: view)
    }

    static func validateScope(_ sample: PenSample) throws {
        guard active, let view = targetView, let window = targetWindow,
              view.window === window, window.isKeyWindow,
              window.windowScene?.activationState == .foregroundActive,
              window.bounds == windowBounds, view.convert(view.bounds, to: window) == targetFrame,
              hits(sample, view: view, window: window) else {
            finish(); throw PenError.target
        }
        guard hooksIntact else { finish(); throw PenError.conflict }
    }

    static func arm(_ value: PenSample) throws {
        try validateScope(value)
        guard let timestamp = value.eventTimestamp, timestamp.isFinite,
              timestamp - lastTimestamp > 0.000000002,
              let timestampKey = timestampKey(timestamp),
              abs(ProcessInfo.processInfo.systemUptime - timestamp) < 5 else {
            finish(); throw PenError.timestamp
        }
        guard nextSample < samples.count else { finish(); throw PenError.invalid }
        let expected = samples[nextSample]
        guard expected.phase == value.phase, expected.x == value.x, expected.y == value.y,
              expected.pressure == value.pressure, expected.altitude == value.altitude,
              expected.azimuth == value.azimuth, expected.roll == value.roll else { finish(); throw PenError.invalid }
        if value.phase == "down" { endHover(cancelled: false) }
        lastTimestamp = timestamp; nextSample += 1
        armed[timestampKey] = value
    }

    static func finish() {
        endHover(cancelled: true)
        runId = nil; samples.removeAll(); armed.removeAll(); bindings.removeAllObjects()
        nextSample = 0; lastTimestamp = 0
        targetView = nil; targetWindow = nil; hoverRecognizer = nil; hoverName = nil
        hoverBinding = nil; terminalHoverState = nil
    }

    static func timestampKey(_ value: Double) -> Int64? {
        guard value.isFinite, value > 0, value < Double(Int64.max / 1_000_000_000 - 1) else { return nil }
        return Int64((value * 1_000_000_000).rounded())
    }

    static func sample(for touch: UITouch) -> PenSample? {
        guard active, hooksIntact, touch.type == .pencil,
              let view = targetView, let window = targetWindow, touch.window === window,
              view.window === window, window.isKeyWindow,
              window.windowScene?.activationState == .foregroundActive,
              window.bounds == windowBounds, view.convert(view.bounds, to: window) == targetFrame,
              let touched = touch.view, touched === view || touched.isDescendant(of: view) else { return nil }
        let phase: String
        switch touch.phase { case .began: phase = "down"; case .moved: phase = "move"; case .ended: phase = "up"; case .cancelled: phase = "cancel"; default: return nil }
        let point = touch.location(in: window)
        func matches(_ value: PenSample) -> Bool {
            value.phase == phase && abs(value.x - point.x / window.bounds.width) < 0.004
                && abs(value.y - point.y / window.bounds.height) < 0.004
        }
        if let binding = bindings.object(forKey: touch), binding.timestamp == touch.timestamp, matches(binding.sample) {
            return binding.sample
        }
        // mach ticks -> seconds can differ by a floating-point rounding step in UIKit.
        // Permit at most 1 ns of conversion error, with no positional/time-window fallback.
        // Coalesced objects can read the same immutable transport-stamped sample.
        guard let key = timestampKey(touch.timestamp) else { return nil }
        let candidates = [key - 1, key, key + 1].compactMap { armed[$0] }.filter {
            guard let timestamp = $0.eventTimestamp else { return false }
            return abs(timestamp - touch.timestamp) <= 0.000000001 && matches($0)
        }
        guard candidates.count == 1, let value = candidates.first else { return nil }
        bindings.setObject(PenTouchBinding(timestamp: touch.timestamp, sample: value), forKey: touch)
        return value
    }

    static func hover(_ value: PenSample) throws -> Int {
        try validateScope(value)
        guard value.phase.hasPrefix("hover") else { throw PenError.invalid }
        if value.phase == "hoverExit" {
            let hadHover = ownedHoverState != nil
            if hadHover, let window = targetWindow { hoverBinding = PenHoverBinding(value, window: window) }
            endHover(cancelled: false)
            return hadHover ? 1 : 0
        }
        guard let recognizer = hoverRecognizer, recognizer.view === targetView,
              recognizer.name == hoverName, recognizer.isEnabled,
              recognizer.allowedTouchTypes.contains(NSNumber(value: UITouch.TouchType.pencil.rawValue)),
              (ownedHoverState.map { recognizer.state == $0 } ?? (recognizer.state == .possible)),
              let window = targetWindow else { finish(); throw PenError.hover }
        guard value.phase == "hoverEnter" ? ownedHoverState == nil : (ownedHoverState != nil || (nextSample > 0 && samples[nextSample - 1].phase == "up")) else { finish(); throw PenError.invalid }
        hoverBinding = PenHoverBinding(value, window: window)
        let state: UIGestureRecognizer.State = ownedHoverState == nil ? .began : .changed
        ownedHoverState = state; terminalHoverState = nil
        recognizer.state = state
        captureHover(recognizer)
        return 1
    }

    static func endHover(cancelled: Bool) {
        // Only finish a sequence we began, on the exact recognizer with the state we own.
        if let recognizer = hoverRecognizer, let state = ownedHoverState,
           recognizer.view === targetView, recognizer.name == hoverName,
           recognizer.isEnabled, recognizer.state == state, hooksIntact {
            terminalHoverState = cancelled ? .cancelled : .ended
            recognizer.state = terminalHoverState!
            captureHover(recognizer)
        }
        // UIKit delivers target/action after this command returns. Keep the final
        // sample readable only in its terminal state, until reset or the next run.
        ownedHoverState = nil
    }

    static func captureHover(_ recognizer: UIHoverGestureRecognizer) {
        // Capture the selected recognizer's actual properties without driving the SDK's
        // observer recognizer or any other app recognizer through a synthetic gesture.
        let selector = NSSelectorFromString("ansightCaptureHoverFromRecognizer:")
        for observer in targetWindow?.gestureRecognizers ?? [] where observer.responds(to: selector) {
            observer.perform(selector, with: recognizer)
        }
    }

    static func hoverSample(_ recognizer: UIHoverGestureRecognizer) -> PenHoverBinding? {
        guard active, hooksIntact, recognizer === hoverRecognizer,
              recognizer.view === targetView, recognizer.name == hoverName, recognizer.isEnabled,
              let view = targetView, let window = targetWindow, view.window === window,
              window.isKeyWindow, window.windowScene?.activationState == .foregroundActive,
              window.bounds == windowBounds, view.convert(view.bounds, to: window) == targetFrame,
              recognizer.state == ownedHoverState || recognizer.state == terminalHoverState else { return nil }
        return hoverBinding
    }
    static func angle(_ sample: PenSample, in view: UIView?, window: UIWindow?) -> CGFloat {
        guard let view, let window else { return sample.azimuth }
        let origin = view.convert(CGPoint.zero, from: window)
        let end = view.convert(CGPoint(x: cos(sample.azimuth), y: sin(sample.azimuth)), from: window)
        return atan2(end.y - origin.y, end.x - origin.x)
    }
    private static func install(includeHover: Bool) throws {
        guard hooksIntact else { throw PenError.conflict }
        var candidates: [PenHook] = []
        func add(_ type: AnyClass, _ original: Selector, _ replacement: Selector) throws {
            if hooks.contains(where: { $0.type === type && $0.selector == original }) { return }
            candidates.append(try PenHook(type, original, replacement))
        }
        try add(UITouch.self, #selector(getter: UITouch.force), #selector(UITouch.ansightPenForce))
        try add(UITouch.self, #selector(getter: UITouch.maximumPossibleForce), #selector(UITouch.ansightPenMaximum))
        try add(UITouch.self, #selector(getter: UITouch.altitudeAngle), #selector(UITouch.ansightPenAltitude))
        try add(UITouch.self, #selector(UITouch.azimuthAngle(in:)), #selector(UITouch.ansightPenAzimuth(in:)))
        try add(UITouch.self, #selector(UITouch.azimuthUnitVector(in:)), #selector(UITouch.ansightPenVector(in:)))
        if #available(iOS 17.5, *) { try add(UITouch.self, #selector(getter: UITouch.rollAngle), #selector(UITouch.ansightPenRoll)) }
        if includeHover {
            guard #available(iOS 16.4, *) else { throw PenError.unavailable }
            try add(UIHoverGestureRecognizer.self, #selector(UIHoverGestureRecognizer.location(in:)), #selector(UIHoverGestureRecognizer.ansightPenLocation(in:)))
            try add(UIHoverGestureRecognizer.self, #selector(getter: UIHoverGestureRecognizer.altitudeAngle), #selector(UIHoverGestureRecognizer.ansightPenAltitude))
            try add(UIHoverGestureRecognizer.self, #selector(UIHoverGestureRecognizer.azimuthAngle(in:)), #selector(UIHoverGestureRecognizer.ansightPenAzimuth(in:)))
            try add(UIHoverGestureRecognizer.self, #selector(getter: UIHoverGestureRecognizer.zOffset), #selector(UIHoverGestureRecognizer.ansightPenDistance))
            if #available(iOS 17.5, *) { try add(UIHoverGestureRecognizer.self, #selector(getter: UIHoverGestureRecognizer.rollAngle), #selector(UIHoverGestureRecognizer.ansightPenRoll)) }
        }
        candidates.forEach { $0.install() }
        hooks.append(contentsOf: candidates)
    }
}

@MainActor
private final class PenHook {
    let type: AnyClass
    let selector: Selector
    let replacement: Method
    let originalIMP: IMP
    let replacementIMP: IMP
    init(_ type: AnyClass, _ selector: Selector, _ replacementSelector: Selector) throws {
        guard let original = class_getInstanceMethod(type, selector),
              let replacement = class_getInstanceMethod(type, replacementSelector) else { throw PenError.conflict }
        let implementation = method_getImplementation(original)
        var info = Dl_info()
        guard dladdr(unsafeBitCast(implementation, to: UnsafeRawPointer.self), &info) != 0,
              let image = info.dli_fname else { throw PenError.conflict }
        let path = String(cString: image)
        guard path.contains("/UIKitCore.framework/") || path.contains("/UIKit.framework/") else { throw PenError.conflict }
        self.type = type; self.selector = selector; self.replacement = replacement
        originalIMP = implementation; replacementIMP = method_getImplementation(replacement)
    }
    var isIntact: Bool {
        guard let original = class_getInstanceMethod(type, selector) else { return false }
        return method_getImplementation(original) == replacementIMP && method_getImplementation(replacement) == originalIMP
    }
    func install() {
        // Add an override when the original is inherited; never mutate a superclass method.
        let encoding = method_getTypeEncoding(replacement)
        if !class_addMethod(type, selector, replacementIMP, encoding), let original = class_getInstanceMethod(type, selector) {
            method_setImplementation(original, replacementIMP)
        }
        method_setImplementation(replacement, originalIMP)
    }
}

@MainActor
private final class PenTouchBinding: NSObject {
    let timestamp: Double
    let sample: PenSample
    init(timestamp: Double, sample: PenSample) { self.timestamp = timestamp; self.sample = sample }
}

@MainActor
private final class PenHoverBinding: NSObject {
    let sample: PenSample
    weak var window: UIWindow?
    init(_ sample: PenSample, window: UIWindow) { self.sample = sample; self.window = window }
}
private extension UITouch {
    @objc func ansightInputSource() -> NSString? {
        PenOverrides.sample(for: self) == nil ? nil : "simulator-hid+sdk"
    }
    @objc func ansightPenForce() -> CGFloat { PenOverrides.sample(for: self)?.pressure ?? ansightPenForce() }
    @objc func ansightPenMaximum() -> CGFloat { PenOverrides.sample(for: self) == nil ? ansightPenMaximum() : 1 }
    @objc func ansightPenAltitude() -> CGFloat { PenOverrides.sample(for: self)?.altitude ?? ansightPenAltitude() }
    @objc func ansightPenAzimuth(in view: UIView?) -> CGFloat {
        guard let sample = PenOverrides.sample(for: self) else { return ansightPenAzimuth(in: view) }
        return PenOverrides.angle(sample, in: view, window: window)
    }
    @objc func ansightPenVector(in view: UIView?) -> CGVector {
        guard let sample = PenOverrides.sample(for: self) else { return ansightPenVector(in: view) }
        let angle = PenOverrides.angle(sample, in: view, window: window)
        return CGVector(dx: cos(angle), dy: sin(angle))
    }
    @objc func ansightPenRoll() -> CGFloat { PenOverrides.sample(for: self)?.roll ?? ansightPenRoll() }
}
private extension UIHoverGestureRecognizer {
    @objc func ansightInputSource() -> NSString? {
        PenOverrides.hoverSample(self) == nil ? nil : "simulator-sdk-hover"
    }
    @objc func ansightPenLocation(in view: UIView?) -> CGPoint {
        guard let binding = PenOverrides.hoverSample(self), let window = binding.window else { return ansightPenLocation(in: view) }
        let point = CGPoint(x: binding.sample.x * window.bounds.width, y: binding.sample.y * window.bounds.height)
        return view?.convert(point, from: window) ?? window.convert(point, to: nil)
    }
    @objc func ansightPenAltitude() -> CGFloat { PenOverrides.hoverSample(self)?.sample.altitude ?? ansightPenAltitude() }
    @objc func ansightPenDistance() -> CGFloat { PenOverrides.hoverSample(self)?.sample.distance ?? ansightPenDistance() }
    @objc func ansightPenAzimuth(in view: UIView?) -> CGFloat {
        guard let binding = PenOverrides.hoverSample(self) else { return ansightPenAzimuth(in: view) }
        return PenOverrides.angle(binding.sample, in: view, window: binding.window)
    }
    @objc func ansightPenRoll() -> CGFloat { PenOverrides.hoverSample(self)?.sample.roll ?? ansightPenRoll() }
}

#endif
