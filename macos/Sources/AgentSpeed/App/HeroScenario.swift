import Foundation

/// Scripted live values for hero vehicle reviews (the same tours as the designer's prototype, at R = 400).
struct HeroScenario: Sendable {
    let name: String
    /// (seconds, tok/s) keyframes; each change eases over 0.45 s (log-space between two non-zero values).
    let keys: [(TimeInterval, Double)]
    let duration: TimeInterval
    /// Holds each keyframe without easing or noise (the title-badge prototype's sampler).
    var stepped = false

    /// Every title upward, a crash back to idle, and a restart.
    static let tour = HeroScenario(name: "tour", keys: [
        (0, 0), (3, 3), (6, 10), (9.5, 14), (12, 19), (14.5, 27), (17, 37), (19.5, 56), (22, 78), (24.5, 112),
        (27, 160), (29.5, 235), (32, 320), (34.5, 440), (37.5, 700), (41, 230), (44, 26), (46.5, 0), (52, 38), (56, 38),
    ], duration: 56)

    /// Start-ups from idle onto different vehicles, with parking in between.
    static let startups = HeroScenario(name: "startups", keys: [
        (0, 0), (2, 10), (5, 0), (10.5, 3), (13, 0), (18.5, 450), (22, 0), (27.5, 18), (30.5, 0), (36, 120), (39, 120),
    ], duration: 39)

    /// The title-badge prototype's stairs at R = 400: level 1, then one title up every 0.9 s, overdrive at 1.9 R,
    /// a fall to titles 9 and 6, and parking.
    static let stairs: HeroScenario = {
        func enter(_ level: Int) -> Double { SpeedTitle(rawValue: level)?.enterSpeed(redline: 400) ?? 0 }
        var keys: [(TimeInterval, Double)] = [(0, 0), (1.6, 1)]
        var t = 3.0
        for level in 2...13 {
            keys.append((t, enter(level) * 1.08))
            t += 0.9
        }
        keys.append((t, 400 * 1.9))
        keys.append((t + 2.4, enter(9) * 1.02))
        keys.append((t + 4.2, enter(6) * 1.02))
        keys.append((t + 6.0, 0))
        return HeroScenario(name: "stairs", keys: keys, duration: t + 10.6, stepped: true)
    }()

    static func named(_ name: String) -> HeroScenario? {
        [tour, startups, stairs].first { $0.name == name }
    }

    func value(at t: TimeInterval) -> Double {
        if stepped { return keys.last { $0.0 <= t }?.1 ?? 0 }
        var v = keys[keys.count - 1].1
        for index in 0..<(keys.count - 1) where t >= keys[index].0 && t < keys[index + 1].0 {
            let previous = index > 0 ? keys[index - 1].1 : keys[index].1
            let current = keys[index].1
            let u = min(1, (t - keys[index].0) / 0.45)
            let eased = u * u * (3 - 2 * u)
            v = previous > 0 && current > 0
                ? exp(log(previous) + (log(current) - log(previous)) * eased)
                : previous + (current - previous) * eased
            break
        }
        guard v > 0 else { return 0 }
        let noise = 0.035 * sin(t * 2.1) + 0.025 * sin(t * 5.3 + 1) + 0.015 * sin(t * 11.7 + 2)
        return v * (1 + noise)
    }
}
