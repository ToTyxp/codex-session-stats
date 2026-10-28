import AgentSpeedCore
import Foundation

struct ProbeArguments {
    enum Mode {
        case dumpToday
        case dumpHistory
        case watch(seconds: Int)
        /// Live network traffic for real (METRICS 10).
        case traffic(seconds: Int)
        /// One real price-table sync round into a scratch folder (NETWORK 3).
        case syncPrices(directory: String)
        /// One real exchange-rate round into a scratch folder (NETWORK 4).
        case syncRates(directory: String, currency: String)
    }

    var mode: Mode
    var configuration: MonitorConfiguration
    /// `--utc`: group history days in UTC instead of the local time zone.
    var utc = false
    /// `--history-store PATH`: reuse (and update) a persistent history store instead of analysing in memory.
    var historyStore: String?
    /// `--prices PATH`: price with this table instead of the bundled one (METRICS 9), as `reference.py --prices`.
    var prices: String?
    /// `--traffic-interval N`: a fixed nettop interval (0 forces standby) instead of the adaptive cadence.
    var trafficInterval: Int?
    /// `--no-live`: traffic without the live transcript monitor (no per-agent attribution), to measure its own cost.
    var noLive = false

    static let usage = """
        usage: AgentSpeedProbe (--dump-today | --dump-history [--utc] [--history-store PATH] | --watch SECONDS
                                | --traffic SECONDS [--traffic-interval 0|1|5] [--no-live] [--history-store PATH])
                               [--prices PATH] [--codex PATH] [--claude PATH] [--dsh PATH] [--opencode PATH]
                               [--gemini PATH] [--omp PATH] [--antigravity PATH] [--workbuddy PATH] [--workbuddy-ai PATH]
                               [--zcode PATH] [--kimi PATH] [--grok-build PATH]
               AgentSpeedProbe --sync-prices DIR | --sync-rates DIR CURRENCY
        """

    static func parse(_ arguments: [String]) -> ProbeArguments? {
        var mode: Mode?
        var configuration = MonitorConfiguration()
        var utc = false
        var historyStore: String?
        var prices: String?
        var trafficInterval: Int?
        var noLive = false
        var iterator = arguments.makeIterator()
        while let argument = iterator.next() {
            switch argument {
            case "--dump-today":
                mode = .dumpToday
            case "--dump-history":
                mode = .dumpHistory
            case "--utc":
                utc = true
            case "--history-store":
                guard let path = iterator.next() else { return nil }
                historyStore = path
            case "--prices":
                guard let path = iterator.next() else { return nil }
                prices = path
            case "--sync-prices":
                guard let directory = iterator.next() else { return nil }
                mode = .syncPrices(directory: directory)
            case "--sync-rates":
                guard let directory = iterator.next(), let currency = iterator.next() else { return nil }
                mode = .syncRates(directory: directory, currency: currency.uppercased())
            case "--watch":
                guard let value = iterator.next(), let seconds = Int(value), seconds > 0, seconds <= 86_400 else { return nil }
                mode = .watch(seconds: seconds)
            case "--traffic":
                guard let value = iterator.next(), let seconds = Int(value), seconds > 0, seconds <= 86_400 else { return nil }
                mode = .traffic(seconds: seconds)
            case "--traffic-interval":
                guard let value = iterator.next(), let seconds = Int(value), [0, 1, 2, 5].contains(seconds) else { return nil }
                trafficInterval = seconds
            case "--no-live":
                noLive = true
            case "--codex":
                guard let path = iterator.next() else { return nil }
                configuration.codexRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--claude":
                guard let path = iterator.next() else { return nil }
                configuration.claudeRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--dsh":
                guard let path = iterator.next() else { return nil }
                configuration.dshRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--gemini":
                // One Gemini CLI `tmp` folder; it replaces both defaults.
                guard let path = iterator.next() else { return nil }
                configuration.geminiRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--omp":
                // One omp `sessions` folder; it replaces the default root and every profile.
                guard let path = iterator.next() else { return nil }
                configuration.ompRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--antigravity":
                // One Antigravity store folder or its `conversations` folder; it replaces the three default stores.
                guard let path = iterator.next() else { return nil }
                configuration.antigravityRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--workbuddy":
                // WorkBuddy's data folder or its `projects` folder.
                guard let path = iterator.next() else { return nil }
                configuration.workbuddyRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--workbuddy-ai":
                // WorkBuddy AI's data folder or its `projects` folder.
                guard let path = iterator.next() else { return nil }
                configuration.workbuddyAiRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--zcode":
                guard let path = iterator.next() else { return nil }
                configuration.zcodeRoot = URL(fileURLWithPath: path)
            case "--kimi":
                guard let path = iterator.next() else { return nil }
                configuration.kimiRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--grok-build":
                guard let path = iterator.next() else { return nil }
                configuration.grokBuildRoot = URL(fileURLWithPath: path, isDirectory: true)
            case "--opencode":
                // The database file (as in reference.py) or the folder that contains it.
                guard let path = iterator.next() else { return nil }
                configuration.opencodeRoot = URL(fileURLWithPath: path)
            default:
                return nil
            }
        }
        guard let mode else { return nil }
        return ProbeArguments(mode: mode, configuration: configuration, utc: utc, historyStore: historyStore, prices: prices,
                              trafficInterval: trafficInterval, noLive: noLive)
    }
}

extension ProbeArguments {
    /// The table of `--prices`, else the bundled one; exits when the file does not validate.
    func priceTable() -> PriceTable {
        guard let prices else { return PriceBook.bundled }
        guard let data = FileManager.default.contents(atPath: prices) else {
            FileHandle.standardError.write(Data("cannot read \(prices)\n".utf8))
            exit(2)
        }
        switch PriceTable.load(data) {
        case .success(let table):
            return table
        case .failure(let error):
            FileHandle.standardError.write(Data("\(prices) is not a valid price table: \(error)\n".utf8))
            exit(2)
        }
    }
}
