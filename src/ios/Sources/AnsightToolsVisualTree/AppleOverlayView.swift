#if canImport(UIKit)
import UIKit

internal final class AppleOverlayView: UIView {
    private let entry: AnsightVisualTreeOverlay

    init(entry: AnsightVisualTreeOverlay, frame: CGRect) {
        self.entry = entry
        super.init(frame: frame)
        backgroundColor = .clear
        isOpaque = false
        isUserInteractionEnabled = false
        accessibilityElementsHidden = true
        isAccessibilityElement = false
        autoresizingMask = [.flexibleWidth, .flexibleHeight]
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        nil
    }

    override func hitTest(_ point: CGPoint, with event: UIEvent?) -> UIView? {
        nil
    }

    override func point(inside point: CGPoint, with event: UIEvent?) -> Bool {
        false
    }

    override func draw(_ rect: CGRect) {
        for rectangle in entry.rectangles {
            let cgRect = CGRect(
                x: rectangle.x,
                y: rectangle.y,
                width: rectangle.width,
                height: rectangle.height
            )
            let path = UIBezierPath(
                roundedRect: cgRect,
                cornerRadius: CGFloat(entry.style.cornerRadius)
            )

            if let fillColor = entry.style.fillColor {
                fillColor.uiColor.setFill()
                path.fill()
            }

            if entry.style.strokeWidth > 0 {
                entry.style.strokeColor.uiColor.setStroke()
                path.lineWidth = CGFloat(entry.style.strokeWidth)
                path.stroke()
            }
        }
    }
}
#endif
