import AnsightPencil
import Flutter

public final class AnsightPencilPlugin: NSObject, FlutterPlugin {
    public static func register(with registrar: FlutterPluginRegistrar) {
        let channel = FlutterMethodChannel(name: "ai.ansight/pencil", binaryMessenger: registrar.messenger())
        registrar.addMethodCallDelegate(AnsightPencilPlugin(), channel: channel)
    }

    public func handle(_ call: FlutterMethodCall, result: @escaping FlutterResult) {
        switch call.method {
        case "requirePlayback":
            do { try AnsightPencil.requireFrameworkPlayback(); result(true) }
            catch { result(FlutterError(code: "pen_playback_unavailable", message: error.localizedDescription, details: nil)) }
        case "capture":
            do { try AnsightPencil.captureFrameworkSample(call.arguments as? String ?? ""); result(true) }
            catch { result(FlutterError(code: "pen_capture_failed", message: error.localizedDescription, details: nil)) }
        case "isSupported": result(AnsightPencil.isSupported)
        case "register":
            do { result(try AnsightPencil.register()) }
            catch { result(FlutterError(code: "pen_registration_failed", message: error.localizedDescription, details: nil)) }
        default: result(FlutterMethodNotImplemented)
        }
    }
}
