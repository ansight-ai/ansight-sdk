import AnsightCore
import Foundation

#if canImport(UIKit)
import UIKit
#endif

internal struct AnsightOverlayColor: Sendable, Equatable {
    let a: Int
    let r: Int
    let g: Int
    let b: Int

    var hexString: String {
        String(format: "#%02X%02X%02X%02X", a, r, g, b)
    }

    #if canImport(UIKit)
    var uiColor: UIColor {
        UIColor(
            red: CGFloat(r) / 255.0,
            green: CGFloat(g) / 255.0,
            blue: CGFloat(b) / 255.0,
            alpha: CGFloat(a) / 255.0
        )
    }
    #endif
}
