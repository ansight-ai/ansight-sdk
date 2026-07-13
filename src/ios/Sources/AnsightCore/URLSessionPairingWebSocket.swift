import CryptoKit
import Foundation
import Security

final class URLSessionPairingWebSocket: PairingWebSocket, @unchecked Sendable {
    private let session: URLSession
    private let sessionDelegate: PairingPinnedSessionDelegate?
    private let task: URLSessionWebSocketTask

    init(url: URL, tlsSpkiSha256: String? = nil) {
        if let tlsSpkiSha256 {
            let delegate = PairingPinnedSessionDelegate(expectedTlsSpkiSha256: tlsSpkiSha256)
            let configuration = URLSessionConfiguration.ephemeral
            configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
            configuration.urlCache = nil
            let session = URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
            self.session = session
            sessionDelegate = delegate
            task = session.webSocketTask(with: url)
        } else {
            session = .shared
            sessionDelegate = nil
            task = session.webSocketTask(with: url)
        }
    }

    func resume() {
        task.resume()
    }

    func cancel(with closeCode: URLSessionWebSocketTask.CloseCode, reason: Data?) {
        task.cancel(with: closeCode, reason: reason)
    }

    func send(_ message: URLSessionWebSocketTask.Message) async throws {
        try await task.send(message)
    }

    func receive() async throws -> URLSessionWebSocketTask.Message {
        try await task.receive()
    }
}

private final class PairingPinnedSessionDelegate: NSObject, URLSessionDelegate, @unchecked Sendable {
    private static let p256SpkiPrefix = Data([
        0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE,
        0x3D, 0x02, 0x01, 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D,
        0x03, 0x01, 0x07, 0x03, 0x42, 0x00,
    ])

    private let expectedPin: Data

    init(expectedTlsSpkiSha256: String) {
        expectedPin = SecurePairingProtocol.decodedBase64URL(expectedTlsSpkiSha256) ?? Data()
    }

    func urlSession(
        _ session: URLSession,
        didReceive challenge: URLAuthenticationChallenge,
        completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
    ) {
        guard challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              expectedPin.count == 32,
              let trust = challenge.protectionSpace.serverTrust,
              let leaf = SecTrustGetCertificateAtIndex(trust, 0),
              let key = SecCertificateCopyKey(leaf),
              let keyBytes = SecKeyCopyExternalRepresentation(key, nil) as Data?,
              keyBytes.count == 65,
              keyBytes.first == 0x04
        else {
            completionHandler(.cancelAuthenticationChallenge, nil)
            return
        }

        let spki = Self.p256SpkiPrefix + keyBytes
        let actualPin = Data(SHA256.hash(data: spki))
        guard constantTimeEqual(actualPin, expectedPin) else {
            completionHandler(.cancelAuthenticationChallenge, nil)
            return
        }

        SecTrustSetPolicies(trust, SecPolicyCreateSSL(true, nil))
        SecTrustSetAnchorCertificates(trust, [leaf] as CFArray)
        SecTrustSetAnchorCertificatesOnly(trust, true)
        guard SecTrustEvaluateWithError(trust, nil) else {
            completionHandler(.cancelAuthenticationChallenge, nil)
            return
        }

        completionHandler(.useCredential, URLCredential(trust: trust))
    }

    private func constantTimeEqual(_ lhs: Data, _ rhs: Data) -> Bool {
        guard lhs.count == rhs.count else { return false }
        var difference: UInt8 = 0
        for index in lhs.indices {
            difference |= lhs[index] ^ rhs[index]
        }
        return difference == 0
    }
}
