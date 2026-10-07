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
    private let feedbackPlaceholder = UILabel()
    private var headerHeight: NSLayoutConstraint?
    private var feedbackHeight: NSLayoutConstraint?
    private weak var brandHeader: UIView?
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

        feedback.backgroundColor = UIColor(white: 0.15, alpha: 1)
        feedback.textColor = .white
        feedback.font = .preferredFont(forTextStyle: .body)
        feedback.layer.cornerRadius = 8
        feedback.accessibilityLabel = "Annotation feedback"
        feedback.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(feedback)
        feedbackPlaceholder.text = "Describe the issue"
        feedbackPlaceholder.textColor = .lightGray
        feedbackPlaceholder.font = feedback.font
        feedbackPlaceholder.isUserInteractionEnabled = false
        feedbackPlaceholder.translatesAutoresizingMaskIntoConstraints = false
        feedback.addSubview(feedbackPlaceholder)

        let doneBar = UIToolbar()
        doneBar.items = [
            UIBarButtonItem(barButtonSystemItem: .flexibleSpace, target: nil, action: nil),
            UIBarButtonItem(title: "Done", style: .done, target: self, action: #selector(dismissKeyboard))
        ]
        doneBar.sizeToFit()
        feedback.inputAccessoryView = doneBar

        #if SWIFT_PACKAGE
        let brandImage = UIImage(named: "ansight-annotation-icon", in: .module, compatibleWith: nil)
        #else
        let brandImage = UIImage(named: "ansight-annotation-icon", in: Bundle(for: Self.self), compatibleWith: nil)
            ?? UIImage(named: "ansight-annotation-icon")
        #endif
        let brandMark = UIImageView(image: brandImage)
        brandMark.contentMode = .scaleAspectFit
        brandMark.accessibilityLabel = "Ansight logo"
        brandMark.isAccessibilityElement = true
        brandMark.widthAnchor.constraint(equalToConstant: 36).isActive = true
        brandMark.heightAnchor.constraint(equalToConstant: 36).isActive = true

        let title = UILabel()
        title.text = "Ansight Annotation"
        title.textColor = .white
        title.font = .boldSystemFont(ofSize: 17)

        let header = UIStackView(arrangedSubviews: [brandMark, title])
        header.axis = .horizontal
        header.spacing = 8
        header.alignment = .center
        header.backgroundColor = UIColor(white: 0.12, alpha: 1)
        header.layoutMargins = UIEdgeInsets(top: 0, left: 16, bottom: 0, right: 16)
        header.isLayoutMarginsRelativeArrangement = true
        header.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(header)
        brandHeader = header
        let headerHeight = header.heightAnchor.constraint(equalToConstant: 44)
        self.headerHeight = headerHeight
        let feedbackHeight = feedback.heightAnchor.constraint(equalToConstant: 90)
        self.feedbackHeight = feedbackHeight
        NotificationCenter.default.addObserver(self, selector: #selector(editingBegan), name: UITextView.textDidBeginEditingNotification, object: feedback)
        NotificationCenter.default.addObserver(self, selector: #selector(editingEnded), name: UITextView.textDidEndEditingNotification, object: feedback)
        NotificationCenter.default.addObserver(self, selector: #selector(feedbackChanged), name: UITextView.textDidChangeNotification, object: feedback)

        let toolbar = UIStackView(arrangedSubviews: [
            button("Cancel", symbol: "xmark", action: #selector(cancel)),
            button("Undo", symbol: "arrow.uturn.backward", action: #selector(undo)),
            button("Clear", symbol: "trash", action: #selector(clear)),
            button("Save", symbol: "checkmark", action: #selector(save))
        ])
        toolbar.axis = .horizontal
        toolbar.distribution = .fillEqually
        toolbar.backgroundColor = UIColor(white: 0.12, alpha: 1)
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
            header.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor),
            header.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            header.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            headerHeight,
            feedback.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 12),
            feedback.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -12),
            feedback.bottomAnchor.constraint(equalTo: toolbar.topAnchor, constant: -8),
            feedbackHeight,
            feedbackPlaceholder.topAnchor.constraint(equalTo: feedback.topAnchor, constant: 12),
            feedbackPlaceholder.leadingAnchor.constraint(equalTo: feedback.leadingAnchor, constant: 12),
            toolbar.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            toolbar.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            toolbar.bottomAnchor.constraint(equalTo: view.keyboardLayoutGuide.topAnchor),
            toolbar.heightAnchor.constraint(equalToConstant: 50)
        ])
    }

    override func viewDidDisappear(_ animated: Bool) {
        super.viewDidDisappear(animated)
        NotificationCenter.default.removeObserver(self)
        continuation?.resume(returning: nil)
        continuation = nil
    }

    private func button(_ title: String, symbol: String, action: Selector) -> UIButton {
        let button = UIButton(type: .system)
        let configuration = UIImage.SymbolConfiguration(pointSize: 22, weight: .medium)
        button.setImage(UIImage(systemName: symbol, withConfiguration: configuration), for: .normal)
        button.tintColor = title == "Save"
            ? UIColor(red: 250 / 255, green: 67 / 255, blue: 31 / 255, alpha: 1)
            : .white
        button.accessibilityLabel = title
        button.addTarget(self, action: action, for: .touchUpInside)
        return button
    }

    @objc private func cancel() { complete(nil) }
    @objc private func dismissKeyboard() { feedback.resignFirstResponder() }
    @objc private func feedbackChanged() { feedbackPlaceholder.isHidden = !feedback.text.isEmpty }
    @objc private func editingBegan() {
        headerHeight?.constant = 0
        feedbackHeight?.constant = 64
        brandHeader?.isHidden = true
    }
    @objc private func editingEnded() {
        headerHeight?.constant = 44
        feedbackHeight?.constant = 90
        brandHeader?.isHidden = false
    }
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
