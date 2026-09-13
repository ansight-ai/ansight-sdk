import Foundation
#if canImport(UIKit)
import UIKit
#elseif canImport(AppKit)
import AppKit
#endif

// The tool dispatches all native operations to the main thread.
internal final class NativeClipboardBackend: ClipboardBackend {
    private func requireAccess() throws {
#if canImport(UIKit)
        let active = MainActor.assumeIsolated { UIApplication.shared.applicationState == .active }
        guard active else { throw ClipboardError(code: "clipboard_not_focused", message: "Activate the app before accessing its clipboard.") }
#elseif !canImport(AppKit)
        throw ClipboardError(code: "clipboard_platform_unsupported", message: "Clipboard is unsupported on this platform.")
#endif
    }
    func getText() throws -> String? {
        try requireAccess()
#if canImport(UIKit)
        return try MainActor.assumeIsolated {
            let board = UIPasteboard.general
            guard board.hasStrings else { return nil }
            guard let text = board.string else {
                throw ClipboardError(code: "clipboard_unavailable", message: "Clipboard text is unavailable; platform paste permission may be required.")
            }
            return text
        }
#elseif canImport(AppKit)
        return NSPasteboard.general.string(forType: .string)
#else
        return nil
#endif
    }
    func hasText() throws -> Bool {
        try requireAccess()
#if canImport(UIKit)
        return MainActor.assumeIsolated { UIPasteboard.general.hasStrings }
#elseif canImport(AppKit)
        return NSPasteboard.general.availableType(from: [.string]) != nil
#else
        return false
#endif
    }
    func setText(_ text: String) throws {
        try requireAccess()
#if canImport(UIKit)
        MainActor.assumeIsolated { UIPasteboard.general.string = text }
#elseif canImport(AppKit)
        NSPasteboard.general.clearContents()
        guard NSPasteboard.general.setString(text, forType: .string) else {
            throw ClipboardError(code: "clipboard_unavailable", message: "The clipboard write failed.")
        }
#endif
    }
    func clear() throws {
        try requireAccess()
#if canImport(UIKit)
        MainActor.assumeIsolated { UIPasteboard.general.items = [] }
#elseif canImport(AppKit)
        NSPasteboard.general.clearContents()
#endif
    }
}
