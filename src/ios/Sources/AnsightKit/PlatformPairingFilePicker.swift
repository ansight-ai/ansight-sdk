#if canImport(UIKit) && canImport(UniformTypeIdentifiers)
import Foundation
import UIKit
import UniformTypeIdentifiers

@MainActor
final class PlatformPairingFilePicker: NSObject, UIDocumentPickerDelegate {
    private static var activePickers: [PlatformPairingFilePicker] = []

    private var continuation: CheckedContinuation<String?, Error>?
    private var picker: UIDocumentPickerViewController?

    private init(continuation: CheckedContinuation<String?, Error>) {
        self.continuation = continuation
    }

    static func read(request: HostConnectionRequest) async throws -> String? {
        try await withCheckedThrowingContinuation { continuation in
            do {
                let presenter = try PlatformPairingPresenter.presentingViewController()
                let filePicker = PlatformPairingFilePicker(continuation: continuation)
                activePickers.append(filePicker)
                filePicker.present(from: presenter, title: request.title)
            } catch {
                continuation.resume(throwing: error)
            }
        }
    }

    private func present(from presenter: UIViewController, title: String?) {
        let documentTypes: [UTType] = [
            .json,
            .plainText,
            .text,
            .data,
        ]
        let documentPicker = UIDocumentPickerViewController(forOpeningContentTypes: documentTypes, asCopy: true)
        documentPicker.allowsMultipleSelection = false
        documentPicker.delegate = self
        if let title = title?.trimmingCharacters(in: .whitespacesAndNewlines), !title.isEmpty {
            documentPicker.title = title
        }
        picker = documentPicker
        presenter.present(documentPicker, animated: true)
    }

    func documentPicker(_ controller: UIDocumentPickerViewController, didPickDocumentsAt urls: [URL]) {
        guard let url = urls.first else {
            complete(with: .success(nil))
            return
        }

        do {
            let didStartAccessing = url.startAccessingSecurityScopedResource()
            defer {
                if didStartAccessing {
                    url.stopAccessingSecurityScopedResource()
                }
            }

            let data = try Data(contentsOf: url)
            guard let payload = String(data: data, encoding: .utf8) else {
                throw RuntimeError.invalidInput("Pairing config file must be UTF-8 text.")
            }

            complete(with: .success(payload))
        } catch {
            complete(with: .failure(error))
        }
    }

    func documentPickerWasCancelled(_ controller: UIDocumentPickerViewController) {
        complete(with: .success(nil))
    }

    private func complete(with result: Result<String?, Error>) {
        guard let continuation else {
            return
        }

        self.continuation = nil
        picker?.dismiss(animated: true)
        picker = nil
        Self.activePickers.removeAll { $0 === self }

        switch result {
        case .success(let payload):
            continuation.resume(returning: payload)
        case .failure(let error):
            continuation.resume(throwing: error)
        }
    }
}
#endif
