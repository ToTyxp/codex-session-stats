import Foundation

/// Builds one compact JSON object with keys in insertion order, so outputs from both platforms can be diffed.
struct JSONLine {
    private var parts: [String] = []

    mutating func add(_ key: String, _ value: String?) {
        parts.append(Self.quote(key) + ":" + (value.map(Self.quote) ?? "null"))
    }

    mutating func add(_ key: String, _ value: Int64?) {
        parts.append(Self.quote(key) + ":" + (value.map { String($0) } ?? "null"))
    }

    mutating func add(_ key: String, _ value: Int) {
        parts.append(Self.quote(key) + ":" + String(value))
    }

    mutating func add(_ key: String, _ value: Double?) {
        parts.append(Self.quote(key) + ":" + (value.map(Self.number) ?? "null"))
    }

    mutating func add(_ key: String, _ value: Bool) {
        parts.append(Self.quote(key) + ":" + (value ? "true" : "false"))
    }

    mutating func add(_ key: String, raw: String) {
        parts.append(Self.quote(key) + ":" + raw)
    }

    var text: String { "{" + parts.joined(separator: ",") + "}" }

    static func array(_ items: [String]) -> String { "[" + items.joined(separator: ",") + "]" }

    /// Fixed three decimals, trailing zeros trimmed.
    static func number(_ value: Double) -> String {
        guard value.isFinite else { return "null" }
        var text = String(format: "%.3f", locale: Locale(identifier: "en_US_POSIX"), value)
        while text.hasSuffix("0") { text.removeLast() }
        if text.hasSuffix(".") { text.removeLast() }
        return text == "-0" ? "0" : text
    }

    static func quote(_ text: String) -> String {
        var result = "\""
        for scalar in text.unicodeScalars {
            switch scalar {
            case "\"": result += "\\\""
            case "\\": result += "\\\\"
            case "\n": result += "\\n"
            case "\r": result += "\\r"
            case "\t": result += "\\t"
            case _ where scalar.value < 0x20:
                result += String(format: "\\u%04x", scalar.value)
            default:
                result.unicodeScalars.append(scalar)
            }
        }
        return result + "\""
    }
}
