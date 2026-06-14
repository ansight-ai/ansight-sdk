#if canImport(UIKit) && canImport(AVFoundation)
import AVFoundation
import UIKit

@MainActor
final class PlatformPairingQrScannerViewController: UIViewController, @MainActor AVCaptureMetadataOutputObjectsDelegate {
    private let requestedTitle: String
    private var continuation: CheckedContinuation<String?, Error>?
    private let previewContainer = UIView()
    private let statusLabel = UILabel()
    private let cancelButton = UIButton(type: .system)

    private var session: AVCaptureSession?
    private var previewLayer: AVCaptureVideoPreviewLayer?
    private var metadataOutput: AVCaptureMetadataOutput?
    private var didConfigure = false

    private init(requestedTitle: String, continuation: CheckedContinuation<String?, Error>) {
        self.requestedTitle = requestedTitle
        self.continuation = continuation
        super.init(nibName: nil, bundle: nil)
        modalPresentationStyle = .fullScreen
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        nil
    }

    static func scan(request: HostConnectionRequest) async throws -> String? {
        try await withCheckedThrowingContinuation { continuation in
            do {
                let presenter = try PlatformPairingPresenter.presentingViewController()
                let title = request.title?.trimmingCharacters(in: .whitespacesAndNewlines)
                let controller = PlatformPairingQrScannerViewController(
                    requestedTitle: title?.isEmpty == false ? title! : "Scan Pairing QR",
                    continuation: continuation
                )
                presenter.present(controller, animated: true)
            } catch {
                continuation.resume(throwing: error)
            }
        }
    }

    override func viewDidLoad() {
        super.viewDidLoad()

        title = requestedTitle
        view.backgroundColor = .black

        previewContainer.translatesAutoresizingMaskIntoConstraints = false
        previewContainer.backgroundColor = .black

        let overlay = UIView()
        overlay.translatesAutoresizingMaskIntoConstraints = false
        overlay.backgroundColor = UIColor.black.withAlphaComponent(0.7)
        overlay.layer.cornerRadius = 16
        overlay.layer.masksToBounds = true

        statusLabel.translatesAutoresizingMaskIntoConstraints = false
        statusLabel.text = "Point the camera at an Ansight pairing QR code."
        statusLabel.textColor = .white
        statusLabel.textAlignment = .center
        statusLabel.numberOfLines = 0

        cancelButton.translatesAutoresizingMaskIntoConstraints = false
        cancelButton.setTitle("Cancel", for: .normal)
        cancelButton.addTarget(self, action: #selector(cancel), for: .touchUpInside)

        view.addSubview(previewContainer)
        view.addSubview(overlay)
        overlay.addSubview(statusLabel)
        overlay.addSubview(cancelButton)

        NSLayoutConstraint.activate([
            previewContainer.topAnchor.constraint(equalTo: view.topAnchor),
            previewContainer.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            previewContainer.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            previewContainer.trailingAnchor.constraint(equalTo: view.trailingAnchor),

            overlay.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 16),
            overlay.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -16),
            overlay.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor, constant: -24),

            statusLabel.topAnchor.constraint(equalTo: overlay.topAnchor, constant: 12),
            statusLabel.leadingAnchor.constraint(equalTo: overlay.leadingAnchor, constant: 16),
            statusLabel.trailingAnchor.constraint(equalTo: overlay.trailingAnchor, constant: -16),

            cancelButton.topAnchor.constraint(equalTo: statusLabel.bottomAnchor, constant: 8),
            cancelButton.leadingAnchor.constraint(equalTo: overlay.leadingAnchor, constant: 16),
            cancelButton.trailingAnchor.constraint(equalTo: overlay.trailingAnchor, constant: -16),
            cancelButton.bottomAnchor.constraint(equalTo: overlay.bottomAnchor, constant: -12),
        ])
    }

    override func viewDidAppear(_ animated: Bool) {
        super.viewDidAppear(animated)

        guard !didConfigure else {
            startSession()
            return
        }

        didConfigure = true
        Task { @MainActor in
            await configureAndStart()
        }
    }

    override func viewDidDisappear(_ animated: Bool) {
        super.viewDidDisappear(animated)
        stopSession()
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer?.frame = previewContainer.bounds
    }

    private func configureAndStart() async {
        do {
            guard await requestCameraAccessIfNeeded() else {
                complete(with: .failure(RuntimeError.invalidInput("Camera access is required to scan an Ansight pairing QR code.")))
                return
            }

            let captureSession = AVCaptureSession()
            captureSession.sessionPreset = .high

            guard let device = AVCaptureDevice.default(for: .video) else {
                complete(with: .failure(RuntimeError.invalidInput("No camera is available to scan an Ansight pairing QR code.")))
                return
            }

            let input = try AVCaptureDeviceInput(device: device)
            guard captureSession.canAddInput(input) else {
                complete(with: .failure(RuntimeError.invalidInput("Camera input is unavailable for QR scanning.")))
                return
            }
            captureSession.addInput(input)

            let output = AVCaptureMetadataOutput()
            guard captureSession.canAddOutput(output) else {
                complete(with: .failure(RuntimeError.invalidInput("Camera metadata output is unavailable for QR scanning.")))
                return
            }
            captureSession.addOutput(output)
            output.setMetadataObjectsDelegate(self, queue: DispatchQueue.main)
            output.metadataObjectTypes = [.qr]

            let layer = AVCaptureVideoPreviewLayer(session: captureSession)
            layer.videoGravity = .resizeAspectFill
            layer.frame = previewContainer.bounds
            previewContainer.layer.insertSublayer(layer, at: 0)

            session = captureSession
            metadataOutput = output
            previewLayer = layer
            startSession()
        } catch {
            complete(with: .failure(error))
        }
    }

    private func requestCameraAccessIfNeeded() async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            return true
        case .notDetermined:
            return await withCheckedContinuation { continuation in
                AVCaptureDevice.requestAccess(for: .video) { granted in
                    continuation.resume(returning: granted)
                }
            }
        default:
            return false
        }
    }

    private func startSession() {
        guard let session, !session.isRunning else {
            return
        }

        DispatchQueue.global(qos: .userInitiated).async {
            session.startRunning()
        }
    }

    private func stopSession() {
        guard let session, session.isRunning else {
            return
        }

        DispatchQueue.global(qos: .userInitiated).async {
            session.stopRunning()
        }
    }

    @objc
    private func cancel() {
        complete(with: .success(nil))
    }

    func metadataOutput(
        _ output: AVCaptureMetadataOutput,
        didOutput metadataObjects: [AVMetadataObject],
        from connection: AVCaptureConnection
    ) {
        guard let payload = metadataObjects
            .compactMap({ $0 as? AVMetadataMachineReadableCodeObject })
            .first(where: { $0.type == .qr })?
            .stringValue
        else {
            return
        }

        complete(with: .success(payload))
    }

    private func complete(with result: Result<String?, Error>) {
        guard let continuation else {
            return
        }

        self.continuation = nil
        stopSession()
        dismiss(animated: true)

        switch result {
        case .success(let payload):
            continuation.resume(returning: payload)
        case .failure(let error):
            continuation.resume(throwing: error)
        }
    }
}
#endif
