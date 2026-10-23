import AnsightPencil
import Foundation
import React
import UIKit

@objc(AnsightReactNativePencil)
final class AnsightReactNativePencil: NSObject {
    @objc(hitTest:x:y:resolver:rejecter:)
    func hitTest(_ identifier: String, x: Double, y: Double, resolver resolve: @escaping RCTPromiseResolveBlock,
                 rejecter reject: @escaping RCTPromiseRejectBlock) {
        DispatchQueue.main.async {
            let windows = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
                .filter { $0.activationState == .foregroundActive }.flatMap(\.windows).filter(\.isKeyWindow)
            var matches: [UIView] = []
            func visit(_ view: UIView) {
                guard !view.isHidden, view.alpha > 0.01, view.isUserInteractionEnabled else { return }
                if view.accessibilityIdentifier == identifier { matches.append(view) }
                view.subviews.forEach(visit)
            }
            windows.forEach(visit)
            guard matches.count == 1, let target = matches.first, let window = target.window,
                  let hit = window.hitTest(CGPoint(x: x, y: y), with: nil) else { resolve(false); return }
            resolve(hit === target || hit.isDescendant(of: target))
        }
    }

    @objc(requirePlayback:rejecter:)
    func requirePlayback(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        do { try AnsightPencil.requireFrameworkPlayback(); resolve(true) }
        catch { reject("pen_playback_unavailable", error.localizedDescription, error) }
    }

    @objc(capture:resolver:rejecter:)
    func capture(_ json: String, resolver resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        do { try AnsightPencil.captureFrameworkSample(json); resolve(true) }
        catch { reject("pen_capture_failed", error.localizedDescription, error) }
    }

    @objc static func requiresMainQueueSetup() -> Bool { false }

    @objc(isSupported:rejecter:)
    func isSupported(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        resolve(AnsightPencil.isSupported)
    }

    @objc(register:rejecter:)
    func register(_ resolve: RCTPromiseResolveBlock, rejecter reject: RCTPromiseRejectBlock) {
        do { resolve(try AnsightPencil.register()) }
        catch { reject("pen_registration_failed", error.localizedDescription, error) }
    }
}
