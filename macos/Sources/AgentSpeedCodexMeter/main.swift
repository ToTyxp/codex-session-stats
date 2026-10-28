import AgentSpeedCore
import Darwin
import Foundation

private struct Arguments {
    var codexHome: URL
    var cacheFile: URL
    var hostId = "local"
    var thread: String?
    var once = false

    init(_ arguments: [String]) throws {
        var home: String?
        var cache: String?
        var index = 0
        while index < arguments.count {
            let flag = arguments[index]
            if flag == "--once" { once = true; index += 1; continue }
            guard ["--codex-home", "--cache-file", "--host-id", "--thread"].contains(flag),
                  index + 1 < arguments.count, !arguments[index + 1].isEmpty else { throw CLIError.arguments }
            let value = arguments[index + 1]
            switch flag {
            case "--codex-home": home = value
            case "--cache-file": cache = value
            case "--host-id": hostId = value
            case "--thread": thread = value
            default: break
            }
            index += 2
        }
        guard let home, let cache else { throw CLIError.arguments }
        codexHome = URL(fileURLWithPath: home, isDirectory: true)
        cacheFile = URL(fileURLWithPath: cache)
    }
}

private enum CLIError: Error { case arguments }
private let usage = "usage: AgentSpeedCodexMeter --codex-home PATH --cache-file PATH [--host-id local] [--once] [--thread UUID]"
private struct Ready: Encodable {
    let type = "ready"
    let backend = "agent-speed"
    let version = "1.4.1"
}
private struct Command: Decodable {
    let command: String
    let threadId: String
    let hostId: String?
}

private func diagnostic(_ message: String) {
    try? FileHandle.standardError.write(contentsOf: Data((message + "\n").utf8))
}

private func emit(_ value: some Encodable) {
    do {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .withoutEscapingSlashes]
        var data = try encoder.encode(value)
        data.append(0x0A)
        try FileHandle.standardOutput.write(contentsOf: data)
    } catch {
        // A closed consumer must never leave a detached monitor behind.
        _exit(0)
    }
}

do {
    let rawArguments = Array(CommandLine.arguments.dropFirst())
    if rawArguments == ["--help"] || rawArguments == ["-h"] {
        print(usage)
        exit(0)
    }
    if rawArguments == ["--version"] {
        print("1.4.1")
        exit(0)
    }
    let arguments = try Arguments(rawArguments)
    // Cache writes finish before samples are emitted. Termination has no network/flush work to wait for.
    signal(SIGTERM) { _ in _exit(0) }
    signal(SIGINT) { _ in _exit(0) }
    signal(SIGPIPE, SIG_IGN)
    let meter = try CodexMeter(
        codexHome: arguments.codexHome,
        cacheFile: arguments.cacheFile,
        hostId: arguments.hostId,
        onEvent: { emit($0) },
        onDiagnostic: { diagnostic($0) }
    )
    emit(Ready())
    meter.emitRestoredSamples()
    if let thread = arguments.thread { meter.select(threadId: thread) }
    if arguments.once {
        meter.discover(nowMs: Int64(Date().timeIntervalSince1970 * 1000), includeHistory: arguments.thread == nil)
        while meter.processNext() {}
    } else {
        var input = Data()
        var discardingLine = false
        var lastDiscovery = Date.distantPast
        while true {
            var descriptor = pollfd(fd: STDIN_FILENO, events: Int16(POLLIN | POLLHUP), revents: 0)
            let status = Darwin.poll(&descriptor, 1, 100)
            if status < 0 { if errno == EINTR { continue }; break }
            if status > 0, descriptor.revents & Int16(POLLIN | POLLHUP | POLLERR) != 0 {
                var buffer = [UInt8](repeating: 0, count: 4096)
                let count = Darwin.read(STDIN_FILENO, &buffer, buffer.count)
                if count <= 0 { if count < 0 && errno == EINTR { continue }; break }
                for byte in buffer[..<count] {
                    if byte == 0x0A {
                        if !discardingLine, !input.isEmpty {
                            if let command = try? JSONDecoder().decode(Command.self, from: input),
                               command.command == "select", !command.threadId.isEmpty {
                                meter.select(threadId: command.threadId, hostId: command.hostId)
                            } else { diagnostic("invalid command") }
                        }
                        input.removeAll(keepingCapacity: true)
                        discardingLine = false
                    } else if !discardingLine {
                        if input.count < 16_384 { input.append(byte) }
                        else { input.removeAll(keepingCapacity: true); discardingLine = true; diagnostic("command too large") }
                    }
                }
            }
            let now = Date()
            if now.timeIntervalSince(lastDiscovery) >= 2 {
                meter.discover(nowMs: Int64(now.timeIntervalSince1970 * 1000))
                lastDiscovery = now
            }
            meter.processNext()
        }
    }
} catch CLIError.arguments {
    diagnostic(usage)
    exit(2)
} catch {
    diagnostic("meter initialization failed")
    exit(1)
}
