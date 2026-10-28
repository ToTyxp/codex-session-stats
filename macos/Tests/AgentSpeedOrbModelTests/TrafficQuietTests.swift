import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeedOrbModel

@Suite("Orb traffic quiet rule")
struct TrafficQuietTests {
    @Test func quietSamplesNeverShowTheReadout() {
        var rule = OrbTrafficQuietRule()
        let changed1 = rule.apply(TrafficRate(rx: 400, tx: 300), bits: false, at: 0)
        #expect(!changed1)
        #expect(rule.reading == nil)
    }

    @Test func loudSampleFadesInAndQuietOneFadesOutAfterTenSeconds() throws {
        var rule = OrbTrafficQuietRule()
        let changed2 = rule.apply(TrafficRate(rx: 12_400_000, tx: 186_000), bits: false, at: 100)
        #expect(changed2)
        let reading = try #require(rule.reading)
        #expect(reading.rx == .init(number: "12.4", unit: "MB/s"))
        #expect(reading.tx == .init(number: "186", unit: "KB/s"))
        #expect(reading.alpha(at: 100, reduceMotion: false) == 0)
        #expect(reading.alpha(at: 100.25, reduceMotion: false) == 1)
        #expect(reading.isFading(at: 100.1, reduceMotion: false))
        // Quiet samples keep updating the numbers while it is shown, without moving the quiet moment.
        let changed3 = rule.apply(TrafficRate(rx: 200, tx: 100), bits: false, at: 101)
        #expect(changed3)
        #expect(rule.reading?.quietAt == 110)
        #expect(rule.reading?.rx.number == "200")
        #expect(abs((rule.reading?.alpha(at: 110.3, reduceMotion: false) ?? 0) - 0.5) < 1e-9)
        // Once gone, quiet samples change nothing (no frames).
        let changed4 = rule.apply(TrafficRate(rx: 300, tx: 100), bits: false, at: 111)
        #expect(!changed4)
    }

    @Test func loudSampleDuringFadeOutReturnsFromCurrentOpacity() throws {
        var rule = OrbTrafficQuietRule()
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: false, at: 0)
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: false, at: 10.3)
        let reading = try #require(rule.reading)
        #expect(abs(reading.alpha(at: 10.3, reduceMotion: false) - 0.5) < 1e-9)
        #expect(reading.quietAt == 20.3)
    }

    @Test func reduceMotionMakesFadesInstant() throws {
        var rule = OrbTrafficQuietRule()
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: true, at: 0)
        let reading = try #require(rule.reading)
        #expect(reading.rx == .init(number: "40.0", unit: "Kb/s"))
        #expect(reading.alpha(at: 0, reduceMotion: true) == 1)
        #expect(reading.alpha(at: 10, reduceMotion: true) == 0)
        #expect(!reading.isFading(at: 0.1, reduceMotion: true))
    }

    @Test func unavailableClearsTheReadout() {
        var rule = OrbTrafficQuietRule()
        rule.apply(TrafficRate(rx: 5000, tx: 0), bits: false, at: 0)
        let changed5 = rule.apply(nil, bits: false, at: 1)
        #expect(changed5)
        #expect(rule.reading == nil)
    }
}
