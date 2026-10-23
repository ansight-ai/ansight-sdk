import AnsightPencil
import Capacitor
import UIKit

@objc(AnsightCapacitorPencilPlugin)
public final class AnsightCapacitorPencilPlugin: CAPPlugin, CAPBridgedPlugin {
    public let identifier = "AnsightCapacitorPencilPlugin"
    public let jsName = "AnsightPencil"
    public let pluginMethods: [CAPPluginMethod] = [
        CAPPluginMethod(name: "isSupported", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "register", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "requirePlayback", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "capture", returnType: CAPPluginReturnPromise),
        CAPPluginMethod(name: "hitTest", returnType: CAPPluginReturnPromise),
    ]

    @objc public func isSupported(_ call: CAPPluginCall) {
        call.resolve(["value": AnsightPencil.isSupported])
    }

    @objc public func requirePlayback(_ call: CAPPluginCall) {
        do { try AnsightPencil.requireFrameworkPlayback(); call.resolve() }
        catch { call.reject(error.localizedDescription, "pen_playback_unavailable", error) }
    }

    @objc public func capture(_ call: CAPPluginCall) {
        DispatchQueue.main.async {
            do {
                guard let webView = self.bridge?.webView, let window = webView.window,
                      let data = call.getString("json")?.data(using: .utf8),
                      var payload = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                      var sample = payload["sample"] as? [String: Any],
                      var geometry = payload["geometry"] as? [String: Any],
                      let x = sample["x"] as? Double, let y = sample["y"] as? Double else {
                    call.reject("The WebView is unavailable.", "pen_capture_failed"); return
                }
                let point = webView.convert(CGPoint(x: x * webView.bounds.width, y: y * webView.bounds.height), to: window)
                sample["x"] = point.x / window.bounds.width; sample["y"] = point.y / window.bounds.height
                geometry["viewportWidth"] = window.bounds.width; geometry["viewportHeight"] = window.bounds.height
                geometry["scale"] = window.screen.scale
                payload["sample"] = sample; payload["geometry"] = geometry
                let json = try JSONSerialization.data(withJSONObject: payload)
                try AnsightPencil.captureFrameworkSample(String(decoding: json, as: UTF8.self)); call.resolve()
            } catch { call.reject(error.localizedDescription, "pen_capture_failed", error) }
        }
    }

    @objc public func hitTest(_ call: CAPPluginCall) {
        DispatchQueue.main.async {
            guard let webView = self.bridge?.webView, let window = webView.window, window.isKeyWindow,
                  let x = call.getDouble("x"), let y = call.getDouble("y"),
                  let width = call.getDouble("width"), width > 0,
                  let height = call.getDouble("height"), height > 0 else { call.resolve(["value": false]); return }
            let point = webView.convert(CGPoint(x: x / width * webView.bounds.width, y: y / height * webView.bounds.height), to: window)
            let hit = window.hitTest(point, with: nil)
            call.resolve(["value": hit === webView || hit?.isDescendant(of: webView) == true])
        }
    }

    @objc public func register(_ call: CAPPluginCall) {
        do { call.resolve(["value": try AnsightPencil.register()]) }
        catch { call.reject(error.localizedDescription, "pen_registration_failed", error) }
    }
}
