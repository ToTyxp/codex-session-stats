import Foundation
import os

enum ProcessRunnerError: Error, Sendable {
    case timedOut
}

/// Runs system tools with argument arrays, never through a shell, with no inherited environment and no output.
enum ProcessRunner {
    static let environment = ["PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "LC_ALL": "C"]

    /// Waits for the tool and returns its exit status; terminates it after `timeout`.
    static func run(_ executable: String, _ arguments: [String], timeout: TimeInterval = 120) async throws -> Int32 {
        let process = configured(executable, arguments)
        let box = UncheckedProcess(process)
        let gate = ResumeGate<Int32>()
        return try await withCheckedThrowingContinuation { continuation in
            gate.arm(continuation)
            process.terminationHandler = { finished in
                gate.resume(with: .success(finished.terminationStatus))
            }
            do {
                try process.run()
            } catch {
                gate.resume(with: .failure(error))
                return
            }
            DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + timeout) {
                guard box.process.isRunning else { return }
                box.process.terminate()
                gate.resume(with: .failure(ProcessRunnerError.timedOut))
            }
        }
    }

    /// Starts a tool that outlives this app; nothing waits for it.
    static func spawnDetached(_ executable: String, _ arguments: [String]) throws {
        try configured(executable, arguments).run()
    }

    private static func configured(_ executable: String, _ arguments: [String]) -> Process {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.environment = environment
        process.currentDirectoryURL = URL(fileURLWithPath: "/", isDirectory: true)
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        return process
    }
}

/// `Process` is thread-safe for `isRunning` and `terminate()`, which is all the timeout uses.
private struct UncheckedProcess: @unchecked Sendable {
    let process: Process

    init(_ process: Process) {
        self.process = process
    }
}

/// Resumes a continuation exactly once, whichever of exit, launch failure or timeout comes first.
private final class ResumeGate<Value: Sendable>: Sendable {
    private let state = OSAllocatedUnfairLock<CheckedContinuation<Value, any Error>?>(initialState: nil)

    func arm(_ continuation: CheckedContinuation<Value, any Error>) {
        state.withLock { $0 = continuation }
    }

    func resume(with result: Result<Value, any Error>) {
        let continuation = state.withLock { current -> CheckedContinuation<Value, any Error>? in
            defer { current = nil }
            return current
        }
        continuation?.resume(with: result)
    }
}
