import AgentSpeedCore
import Foundation

/// Update traffic (UPDATES sections 1 and 6): one ephemeral session per transfer with no cookies, cache or stored
/// credentials, only the headers set here, every URL (redirects included) checked against the policy, and hard
/// byte limits.
struct UpdateHTTP: Sendable {
    static let requestTimeout: TimeInterval = 15
    static let downloadTimeout: TimeInterval = 15 * 60

    let configuration: UpdateConfiguration

    func feedRequest() -> URLRequest {
        var request = baseRequest(configuration.feedURL, timeout: Self.requestTimeout)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("2022-11-28", forHTTPHeaderField: "X-GitHub-Api-Version")
        return request
    }

    func assetRequest(_ url: URL) -> URLRequest {
        var request = baseRequest(url, timeout: Self.requestTimeout)
        request.setValue("application/octet-stream", forHTTPHeaderField: "Accept")
        return request
    }

    /// Reads a small response into memory. `expectedSize`, when given, must match exactly.
    func data(for request: URLRequest, limit: Int64, expectedSize: Int64? = nil) async throws -> Data {
        let transfer = HTTPTransfer(policy: configuration.policy, limit: limit, expectedSize: expectedSize, destination: nil, progress: nil)
        return try await transfer.run(request, session: session(resourceTimeout: Self.requestTimeout, delegate: transfer))
    }

    /// Streams a response into `destination`, which must not exist yet; exactly `expectedSize` bytes are accepted.
    func download(
        _ request: URLRequest,
        to destination: URL,
        expectedSize: Int64,
        progress: @escaping @Sendable (Double) -> Void
    ) async throws {
        guard FileManager.default.createFile(atPath: destination.path, contents: nil, attributes: [.posixPermissions: 0o600]) else {
            throw HTTPTransferError.write
        }
        let handle = try FileHandle(forWritingTo: destination)
        defer { try? handle.close() }
        let transfer = HTTPTransfer(
            policy: configuration.policy,
            limit: expectedSize,
            expectedSize: expectedSize,
            destination: handle,
            progress: progress
        )
        _ = try await transfer.run(request, session: session(resourceTimeout: Self.downloadTimeout, delegate: transfer))
        try handle.synchronize()
    }

    /// CFNetwork always adds `Accept-Language` from the user's languages; a constant value keeps it from saying
    /// anything about the user. The cache is off, so no `Cache-Control` header is needed either.
    private func baseRequest(_ url: URL, timeout: TimeInterval) -> URLRequest {
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: timeout)
        request.httpMethod = "GET"
        request.httpShouldHandleCookies = false
        request.setValue(configuration.userAgent, forHTTPHeaderField: "User-Agent")
        request.setValue("*", forHTTPHeaderField: "Accept-Language")
        return request
    }

    private func session(resourceTimeout: TimeInterval, delegate: HTTPTransfer) -> URLSession {
        let config = URLSessionConfiguration.ephemeral
        config.httpCookieStorage = nil
        config.httpCookieAcceptPolicy = .never
        config.httpShouldSetCookies = false
        config.urlCredentialStorage = nil
        config.urlCache = nil
        config.requestCachePolicy = .reloadIgnoringLocalCacheData
        config.httpAdditionalHeaders = [:]
        config.timeoutIntervalForRequest = Self.requestTimeout
        config.timeoutIntervalForResource = resourceTimeout
        config.waitsForConnectivity = false
        config.tlsMinimumSupportedProtocolVersion = .TLSv12
        let queue = OperationQueue()
        queue.maxConcurrentOperationCount = 1
        queue.name = "io.github.zoefix.agentspeed.update-transfer"
        return URLSession(configuration: config, delegate: delegate, delegateQueue: queue)
    }
}

enum HTTPTransferError: Error, Equatable, Sendable {
    case status(Int)
    case disallowedURL
    case tooLarge
    case sizeMismatch
    case write
}

/// Delegate of one transfer. All mutable state is touched only on the session's serial delegate queue.
private final class HTTPTransfer: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private let policy: UpdateURLPolicy
    private let limit: Int64
    private let expectedSize: Int64?
    private let destination: FileHandle?
    private let progress: (@Sendable (Double) -> Void)?

    private var buffer = Data()
    private var received: Int64 = 0
    private var reported = -1.0
    private var response: HTTPURLResponse?
    private var failure: HTTPTransferError?
    private var continuation: CheckedContinuation<Data, any Error>?

    init(
        policy: UpdateURLPolicy,
        limit: Int64,
        expectedSize: Int64?,
        destination: FileHandle?,
        progress: (@Sendable (Double) -> Void)?
    ) {
        self.policy = policy
        self.limit = limit
        self.expectedSize = expectedSize
        self.destination = destination
        self.progress = progress
    }

    func run(_ request: URLRequest, session: URLSession) async throws -> Data {
        guard policy.permits(request.url) else {
            session.invalidateAndCancel()
            throw HTTPTransferError.disallowedURL
        }
        let task = session.dataTask(with: request)
        let queue = session.delegateQueue
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                queue.addOperation {
                    self.continuation = continuation
                    task.resume()
                }
            }
        } onCancel: {
            task.cancel()
        }
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        willPerformHTTPRedirection response: HTTPURLResponse,
        newRequest request: URLRequest,
        completionHandler: @escaping @Sendable (URLRequest?) -> Void
    ) {
        guard policy.permits(request.url) else {
            failure = .disallowedURL
            completionHandler(nil)
            task.cancel()
            return
        }
        completionHandler(request)
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        didReceive challenge: URLAuthenticationChallenge,
        completionHandler: @escaping @Sendable (URLSession.AuthChallengeDisposition, URLCredential?) -> Void
    ) {
        if challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust {
            completionHandler(.performDefaultHandling, nil)
        } else {
            completionHandler(.cancelAuthenticationChallenge, nil)
        }
    }

    func urlSession(
        _ session: URLSession,
        dataTask: URLSessionDataTask,
        didReceive response: URLResponse,
        completionHandler: @escaping @Sendable (URLSession.ResponseDisposition) -> Void
    ) {
        guard let http = response as? HTTPURLResponse, policy.permits(http.url) else {
            failure = .disallowedURL
            completionHandler(.cancel)
            return
        }
        self.response = http
        guard http.statusCode == 200 else {
            failure = .status(http.statusCode)
            completionHandler(.cancel)
            return
        }
        let encoded = http.value(forHTTPHeaderField: "Content-Encoding").map { $0.lowercased() != "identity" } ?? false
        if !encoded, http.expectedContentLength > limit {
            failure = .tooLarge
            completionHandler(.cancel)
            return
        }
        completionHandler(.allow)
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        guard failure == nil else { return }
        received += Int64(data.count)
        guard received <= limit else {
            failure = .tooLarge
            dataTask.cancel()
            return
        }
        if let destination {
            do {
                try destination.write(contentsOf: data)
            } catch {
                failure = .write
                dataTask.cancel()
                return
            }
        } else {
            buffer.append(data)
        }
        reportProgress()
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: (any Error)?) {
        session.finishTasksAndInvalidate()
        let result: Result<Data, any Error>
        if let failure {
            result = .failure(failure)
        } else if let error {
            result = .failure(error)
        } else if response == nil {
            result = .failure(URLError(.badServerResponse))
        } else if let expectedSize, received != expectedSize {
            result = .failure(HTTPTransferError.sizeMismatch)
        } else {
            result = .success(buffer)
        }
        continuation?.resume(with: result)
        continuation = nil
    }

    private func reportProgress() {
        guard let progress, let expectedSize, expectedSize > 0 else { return }
        let fraction = min(1, Double(received) / Double(expectedSize))
        guard fraction - reported >= 0.01 || fraction == 1 else { return }
        reported = fraction
        progress(fraction)
    }
}
