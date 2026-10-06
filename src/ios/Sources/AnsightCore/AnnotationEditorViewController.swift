#if canImport(UIKit)
import UIKit

internal struct AnnotationPoint: Sendable {
    let x: Double
    let y: Double
}

internal struct AnnotationDraft: Sendable {
    let feedback: String
    let paths: [[AnnotationPoint]]
}

@MainActor
internal final class AnnotationEditorViewController: UIViewController {
    private let screenshot: AnsightScreenSnapshot?
    private let ink = AnnotationInkView()
    private let feedback = UITextView()
    private var continuation: CheckedContinuation<AnnotationDraft?, Never>?

    private init(screenshot: AnsightScreenSnapshot?) {
        self.screenshot = screenshot
        super.init(nibName: nil, bundle: nil)
        modalPresentationStyle = .fullScreen
    }

    required init?(coder: NSCoder) { nil }

    static func present(screenshot: AnsightScreenSnapshot?) async -> AnnotationDraft? {
        guard let root = UIApplication.shared.connectedScenes
            .compactMap({ $0 as? UIWindowScene })
            .flatMap(\.windows)
            .first(where: { $0.isKeyWindow })?.rootViewController else {
            return nil
        }
        var presenter = root
        while let next = presenter.presentedViewController { presenter = next }
        let editor = AnnotationEditorViewController(screenshot: screenshot)
        return await withCheckedContinuation { continuation in
            editor.continuation = continuation
            presenter.present(editor, animated: true)
        }
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black

        let image = UIImageView(image: screenshot.flatMap { UIImage(data: $0.data) })
        image.contentMode = .scaleToFill
        image.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(image)

        ink.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(ink)

        feedback.backgroundColor = UIColor.black.withAlphaComponent(0.78)
        feedback.textColor = .white
        feedback.font = .preferredFont(forTextStyle: .body)
        feedback.layer.cornerRadius = 8
        feedback.accessibilityLabel = "Annotation feedback"
        feedback.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(feedback)

        let toolbar = UIStackView(arrangedSubviews: [
            button("Cancel", action: #selector(cancel)),
            button("Undo", action: #selector(undo)),
            button("Clear", action: #selector(clear)),
            button("Save", action: #selector(save))
        ])
        toolbar.axis = .horizontal
        toolbar.distribution = .fillEqually
        toolbar.backgroundColor = UIColor.black.withAlphaComponent(0.85)
        toolbar.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(toolbar)

        NSLayoutConstraint.activate([
            image.topAnchor.constraint(equalTo: view.topAnchor),
            image.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            image.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            image.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            ink.topAnchor.constraint(equalTo: image.topAnchor),
            ink.leadingAnchor.constraint(equalTo: image.leadingAnchor),
            ink.trailingAnchor.constraint(equalTo: image.trailingAnchor),
            ink.bottomAnchor.constraint(equalTo: image.bottomAnchor),
            feedback.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 12),
            feedback.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -12),
            feedback.bottomAnchor.constraint(equalTo: toolbar.topAnchor, constant: -8),
            feedback.heightAnchor.constraint(equalToConstant: 90),
            toolbar.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            toolbar.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            toolbar.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor),
            toolbar.heightAnchor.constraint(equalToConstant: 50)
        ])
    }

    override func viewDidDisappear(_ animated: Bool) {
        super.viewDidDisappear(animated)
        continuation?.resume(returning: nil)
        continuation = nil
    }

    private func button(_ title: String, action: Selector) -> UIButton {
        let button = UIButton(type: .system)
        button.setTitle(title, for: .normal)
        button.addTarget(self, action: action, for: .touchUpInside)
        return button
    }

    @objc private func cancel() { complete(nil) }
    @objc private func undo() { ink.undo() }
    @objc private func clear() { ink.clear() }
    @objc private func save() {
        complete(AnnotationDraft(feedback: feedback.text ?? "", paths: ink.paths))
    }

    private func complete(_ draft: AnnotationDraft?) {
        let continuation = self.continuation
        self.continuation = nil
        dismiss(animated: true) { continuation?.resume(returning: draft) }
    }
}

@MainActor
private final class AnnotationInkView: UIView {
    private(set) var paths: [[AnnotationPoint]] = []
    private let stroke = UIColor(red: 1, green: 0.23, blue: 0.18, alpha: 1)

    override init(frame: CGRect) {
        super.init(frame: frame)
        backgroundColor = .clear
        isMultipleTouchEnabled = false
    }

    required init?(coder: NSCoder) { nil }

    override func draw(_ rect: CGRect) {
        stroke.setStroke()
        for points in paths where points.count >= 2 {
            let path = UIBezierPath()
            path.lineWidth = 3
            path.lineCapStyle = .round
            path.lineJoinStyle = .round
            path.move(to: CGPoint(x: CGFloat(points[0].x) * bounds.width, y: CGFloat(points[0].y) * bounds.height))
            for point in points.dropFirst() {
                path.addLine(to: CGPoint(x: CGFloat(point.x) * bounds.width, y: CGFloat(point.y) * bounds.height))
            }
            path.stroke()
        }
    }

    override func touchesBegan(_ touches: Set<UITouch>, with event: UIEvent?) {
        guard let touch = touches.first else { return }
        paths.append([normalized(touch.location(in: self))])
        setNeedsDisplay()
    }

    override func touchesMoved(_ touches: Set<UITouch>, with event: UIEvent?) {
        append(touches)
    }

    override func touchesEnded(_ touches: Set<UITouch>, with event: UIEvent?) {
        append(touches)
    }

    private func append(_ touches: Set<UITouch>) {
        guard let touch = touches.first, !paths.isEmpty else { return }
        paths[paths.count - 1].append(normalized(touch.location(in: self)))
        setNeedsDisplay()
    }

    private func normalized(_ point: CGPoint) -> AnnotationPoint {
        AnnotationPoint(
            x: min(max(Double(point.x / max(bounds.width, 1)), 0), 1),
            y: min(max(Double(point.y / max(bounds.height, 1)), 0), 1)
        )
    }

    func undo() { if !paths.isEmpty { paths.removeLast(); setNeedsDisplay() } }
    func clear() { paths.removeAll(); setNeedsDisplay() }
}
#endif
