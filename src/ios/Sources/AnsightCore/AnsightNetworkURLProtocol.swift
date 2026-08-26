import Foundation

/// Registers the native interceptor only when an app explicitly enables network capture.
enum AnsightNativeNetworkCapture {
    private static let lock = NSLock()
    private nonisolated(unsafe) static var configuredOptions = AnsightNetworkCaptureOptions()
    private nonisolated(unsafe) static var registered = false

    static var options: AnsightNetworkCaptureOptions {
        lock.withLock { configuredOptions }
    }

    static func configure(_ options: AnsightNetworkCaptureOptions) {
        let shouldRegister = options.enabled
        let registrationChange = lock.withLock { () -> Bool? in
            configuredOptions = options
            guard registered != shouldRegister else { return nil }
            registered = shouldRegister
            return shouldRegister
        }

        switch registrationChange {
        case true:
            URLProtocol.registerClass(AnsightNetworkURLProtocol.self)
        case false:
            URLProtocol.unregisterClass(AnsightNetworkURLProtocol.self)
        case nil:
            break
        }
    }

    static func setRedactionEnabled(_ enabled: Bool) {
        lock.withLock {
            configuredOptions.redactSensitiveData = enabled
        }
    }
}

/// Intercepts native HTTP(S) requests handled by Foundation's URL Loading System, forwards the
/// response unchanged, and records bounded evidence after the request completes.
final class AnsightNetworkURLProtocol: URLProtocol, URLSessionDataDelegate, @unchecked Sendable {
    private static let handledRequestProperty = "ai.ansight.network-capture.handled"
    private static let internalTrafficHeaderName = "X-Ansight-Internal-Traffic"

    private var session: URLSession?
    private var dataTask: URLSessionDataTask?
    private var response: HTTPURLResponse?
    private var responseBody = Data()
    private var responseBodySizeBytes: Int64 = 0
    private var startedAt = Date()
    private var startedAtUtc = AnsightClock.isoNow()
    private var networkProtocolName: String?
    private var captureOptions = AnsightNetworkCaptureOptions()
    private var shouldRecord = true

    override class func canInit(with request: URLRequest) -> Bool {
        let options = AnsightNativeNetworkCapture.options
        guard URLProtocol.property(forKey: handledRequestProperty, in: request) == nil,
              let scheme = request.url?.scheme?.lowercased()
        else {
            return false
        }
        guard scheme == "http" || scheme == "https" else { return false }
        if request.value(forHTTPHeaderField: internalTrafficHeaderName) != nil {
            // Intercept solely to strip Ansight's private marker before forwarding. This request
            // is never converted into network evidence.
            return true
        }
        guard options.enabled else { return false }
        return request.value(forHTTPHeaderField: "Sec-WebSocket-Key") == nil
            && request.value(forHTTPHeaderField: "Upgrade")?.caseInsensitiveCompare("websocket") != .orderedSame
    }

    override class func canonicalRequest(for request: URLRequest) -> URLRequest {
        request
    }

    override func startLoading() {
        captureOptions = AnsightNativeNetworkCapture.options
        shouldRecord = request.value(forHTTPHeaderField: Self.internalTrafficHeaderName) == nil
        guard (captureOptions.enabled || !shouldRecord),
              let mutableRequest = (request as NSURLRequest).mutableCopy() as? NSMutableURLRequest
        else {
            client?.urlProtocol(self, didFailWithError: URLError(.cancelled))
            return
        }

        URLProtocol.setProperty(
            true,
            forKey: Self.handledRequestProperty,
            in: mutableRequest
        )
        mutableRequest.setValue(nil, forHTTPHeaderField: Self.internalTrafficHeaderName)
        startedAt = Date()
        startedAtUtc = AnsightClock.isoNow()

        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = (configuration.protocolClasses ?? []).filter {
            $0 != AnsightNetworkURLProtocol.self
        }
        configuration.urlCache = nil
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        let session = URLSession(configuration: configuration, delegate: self, delegateQueue: nil)
        self.session = session
        let task = session.dataTask(with: mutableRequest as URLRequest)
        dataTask = task
        task.resume()
    }

    override func stopLoading() {
        dataTask?.cancel()
        dataTask = nil
        session?.invalidateAndCancel()
        session = nil
    }

    func urlSession(
        _ session: URLSession,
        dataTask: URLSessionDataTask,
        didReceive response: URLResponse,
        completionHandler: @escaping @Sendable (URLSession.ResponseDisposition) -> Void
    ) {
        self.response = response as? HTTPURLResponse
        client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
        completionHandler(.allow)
    }

    func urlSession(
        _ session: URLSession,
        dataTask: URLSessionDataTask,
        didReceive data: Data
    ) {
        responseBodySizeBytes += Int64(data.count)
        let remaining = max(0, captureOptions.maximumBodyBytes - responseBody.count)
        if captureOptions.captureResponseBody && remaining > 0 {
            responseBody.append(data.prefix(remaining))
        }
        client?.urlProtocol(self, didLoad: data)
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        didFinishCollecting metrics: URLSessionTaskMetrics
    ) {
        networkProtocolName = metrics.transactionMetrics.last?.networkProtocolName
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        didCompleteWithError error: (any Error)?
    ) {
        if let error {
            client?.urlProtocol(self, didFailWithError: error)
        } else {
            client?.urlProtocolDidFinishLoading(self)
        }

        if shouldRecord {
            let record = makeNetworkRecord(error: error)
            Task {
                _ = await AnsightRuntime.shared.recordNetworkRequest(record)
            }
        }
        session.finishTasksAndInvalidate()
        self.session = nil
        dataTask = nil
    }

    private func makeNetworkRecord(error: (any Error)?) -> AnsightNetworkRequest {
        let completedAt = Date()
        let requestBody = request.httpBody
        let requestBodySize = contentLength(
            headers: request.allHTTPHeaderFields,
            fallback: requestBody.map { Int64($0.count) }
        )
        let responseBodySize = contentLength(
            headers: response?.allHeaderFields,
            fallback: responseBodySizeBytes
        )
        return AnsightNetworkRequest(
            id: UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased(),
            source: "apple.nsurlprotocol",
            startedAtUtc: startedAtUtc,
            completedAtUtc: AnsightClock.isoNow(),
            durationMilliseconds: max(0, completedAt.timeIntervalSince(startedAt) * 1_000),
            method: request.httpMethod ?? "GET",
            url: captureUrl(request.url),
            protocol: networkProtocolName,
            requestHeaders: captureOptions.includeRequestHeaders
                ? headers(request.allHTTPHeaderFields)
                : [],
            requestBodySizeBytes: captureOptions.includeBodySizes ? requestBodySize : nil,
            requestBody: makeBody(
                requestBody,
                totalBytes: requestBodySize,
                contentType: request.value(forHTTPHeaderField: "Content-Type"),
                enabled: captureOptions.captureRequestBody
            ),
            statusCode: response?.statusCode,
            reasonPhrase: response.map { HTTPURLResponse.localizedString(forStatusCode: $0.statusCode) },
            responseHeaders: captureOptions.includeResponseHeaders
                ? headers(response?.allHeaderFields)
                : [],
            responseBodySizeBytes: captureOptions.includeBodySizes ? responseBodySize : nil,
            responseBody: makeBody(
                responseBody,
                totalBytes: responseBodySize,
                contentType: response?.value(forHTTPHeaderField: "Content-Type"),
                enabled: captureOptions.captureResponseBody
            ),
            errorType: error.map { String(reflecting: type(of: $0)) },
            errorMessage: error?.localizedDescription
        )
    }

    private func captureUrl(_ url: URL?) -> String {
        guard let url else { return "<unknown>" }
        guard !captureOptions.includeQueryString,
              var components = URLComponents(url: url, resolvingAgainstBaseURL: false)
        else {
            return url.absoluteString
        }
        components.query = nil
        return components.url?.absoluteString ?? url.absoluteString
    }

    private func makeBody(
        _ data: Data?,
        totalBytes: Int64?,
        contentType: String?,
        enabled: Bool
    ) -> AnsightNetworkBody? {
        guard enabled,
              captureOptions.maximumBodyBytes > 0,
              let data,
              !data.isEmpty
        else {
            return nil
        }

        let captured = Data(data.prefix(captureOptions.maximumBodyBytes))
        let isText = isTextContentType(contentType)
        guard isText || captureOptions.captureBinaryBodies else { return nil }
        let bodyData: Data
        let encoded: String
        let encoding: String
        if isText {
            bodyData = completeUtf8(captured)
            encoded = String(data: bodyData, encoding: .utf8) ?? ""
            encoding = "utf8"
        } else {
            bodyData = captured
            encoded = captured.base64EncodedString()
            encoding = "base64"
        }
        return AnsightNetworkBody(
            contentType: contentType,
            encoding: encoding,
            data: encoded,
            capturedBytes: Int64(bodyData.count),
            totalBytes: totalBytes,
            truncated: data.count > bodyData.count || (totalBytes.map { $0 > Int64(bodyData.count) } ?? false)
        )
    }

    private func headers(_ values: [AnyHashable: Any]?) -> [AnsightNetworkHeader] {
        (values ?? [:])
            .map {
                AnsightNetworkHeader(
                    name: String(describing: $0.key),
                    value: String(describing: $0.value)
                )
            }
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    private func headers(_ values: [String: String]?) -> [AnsightNetworkHeader] {
        (values ?? [:])
            .map { AnsightNetworkHeader(name: $0.key, value: $0.value) }
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    private func contentLength(headers: [String: String]?, fallback: Int64?) -> Int64? {
        guard let rawValue = headers?.first(where: {
            $0.key.caseInsensitiveCompare("Content-Length") == .orderedSame
        })?.value,
        let value = Int64(rawValue),
        value >= 0
        else {
            return fallback
        }
        return value
    }

    private func contentLength(headers: [AnyHashable: Any]?, fallback: Int64?) -> Int64? {
        guard let rawValue = headers?.first(where: {
            String(describing: $0.key).caseInsensitiveCompare("Content-Length") == .orderedSame
        }).map({ String(describing: $0.value) }),
        let value = Int64(rawValue),
        value >= 0
        else {
            return fallback
        }
        return value
    }

    private func isTextContentType(_ contentType: String?) -> Bool {
        guard let normalized = contentType?.lowercased() else { return true }
        return normalized.hasPrefix("text/")
            || normalized.contains("json")
            || normalized.contains("xml")
            || normalized.contains("javascript")
            || normalized.contains("x-www-form-urlencoded")
            || normalized.contains("graphql")
    }

    private func completeUtf8(_ data: Data) -> Data {
        var length = data.count
        while length > 0 {
            let candidate = Data(data.prefix(length))
            if String(data: candidate, encoding: .utf8) != nil {
                return candidate
            }
            length -= 1
        }
        return Data()
    }
}
