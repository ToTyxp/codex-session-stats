import AgentSpeedCore
import Foundation

/// What an orb's traffic readout shows (DESIGN 10, Floating orb readout): download over upload in fixed cells, with
/// the quiet rule's two moments. Times are monotonic seconds (the app clock).
public struct OrbTrafficReading: Equatable, Sendable {
    public var rx: TrafficFormat.Parts
    public var tx: TrafficFormat.Parts
    /// Rates in bits per second (the unit cell is `Mb/s` wide instead of `MB/s`).
    public var bits: Bool
    /// When the readout began fading in; fully shown `fadeIn` later.
    public var shownAt: TimeInterval
    /// When it begins fading out: 10 s after the last sample at or above 1 KB/s; gone `fadeOut` later.
    public var quietAt: TimeInterval

    public init(rx: TrafficFormat.Parts, tx: TrafficFormat.Parts, bits: Bool, shownAt: TimeInterval, quietAt: TimeInterval) {
        self.rx = rx
        self.tx = tx
        self.bits = bits
        self.shownAt = shownAt
        self.quietAt = quietAt
    }

    public static let fadeIn: TimeInterval = 0.25
    public static let fadeOut: TimeInterval = 0.6

    /// Opacity at `time`; Reduce Motion makes both fades instant.
    public func alpha(at time: TimeInterval, reduceMotion: Bool) -> Double {
        let fadeIn = reduceMotion ? 0 : Self.fadeIn
        let fadeOut = reduceMotion ? 0 : Self.fadeOut
        let incoming = fadeIn > 0 ? ((time - shownAt) / fadeIn) : (time >= shownAt ? 1 : 0)
        let outgoing = fadeOut > 0 ? 1 - ((time - quietAt) / fadeOut) : (time < quietAt ? 1 : 0)
        return min(max(incoming, 0), 1, max(outgoing, 0))
    }

    /// A fade runs at `time` (frames at 30 fps).
    public func isFading(at time: TimeInterval, reduceMotion: Bool) -> Bool {
        guard !reduceMotion else { return false }
        return (time >= shownAt && time < shownAt + Self.fadeIn) || (time >= quietAt && time < quietAt + Self.fadeOut)
    }

    /// Fully faded out at `time`: nothing is drawn and new quiet samples change nothing.
    public func isGone(at time: TimeInterval) -> Bool { time >= quietAt + Self.fadeOut }
}

/// The quiet rule of the orb traffic readout (DESIGN 10): visible while the shown `↓ + ↑` rate is at least 1 KB/s;
/// after 10 s below it fades out (0.6 s) and returns (0.25 s) with the next loud sample. Quiet samples of a gone
/// readout change nothing, so a quiet orb renders no frames.
public struct OrbTrafficQuietRule: Sendable {
    public static let threshold = 1000.0
    public static let quietAfter: TimeInterval = 10

    public private(set) var reading: OrbTrafficReading?

    public init() {}

    /// Applies one sample at `time`; returns whether the reading changed. `rate` nil clears the readout.
    @discardableResult
    public mutating func apply(_ rate: TrafficRate?, bits: Bool, at time: TimeInterval) -> Bool {
        guard let rate else {
            guard reading != nil else { return false }
            reading = nil
            return true
        }
        let rx = TrafficFormat.rateParts(rate.rx, bits: bits)
        let tx = TrafficFormat.rateParts(rate.tx, bits: bits)
        let loud = rate.sum >= Self.threshold
        var next: OrbTrafficReading
        if let current = reading, !current.isGone(at: time) {
            next = current
            next.rx = rx
            next.tx = tx
            next.bits = bits
            if loud {
                if time >= current.quietAt {
                    // Fading out: come back from the current opacity.
                    let alpha = current.alpha(at: time, reduceMotion: false)
                    next.shownAt = time - alpha * OrbTrafficReading.fadeIn
                }
                next.quietAt = time + Self.quietAfter
            }
        } else {
            guard loud else { return false }
            next = OrbTrafficReading(rx: rx, tx: tx, bits: bits, shownAt: time, quietAt: time + Self.quietAfter)
        }
        guard next != reading else { return false }
        reading = next
        return true
    }
}
