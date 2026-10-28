import AgentSpeedCore
import AgentSpeedHero
import AgentSpeedOrbModel
import Foundation
import Metal

/// `--render-previews` group `herobench`: CPU time of the hero vehicle per frame (stage step and draw list) over the
/// prototype tour and at a Max-effects redline, and CPU / GPU time of encoding its layer. Writes `hero-bench.txt`.
@MainActor
enum HeroBenchmark {
    static func run(model: AppModel, into directory: URL) {
        // The designer's scenarios are written for R = 400 tok/s.
        let redline = model.settings.redlineOutput
        model.settings.redlineOutput = 400
        defer { model.settings.redlineOutput = redline }
        var lines: [String] = []
        #if DEBUG
        lines.append("build: debug (use a release build for representative numbers)")
        #else
        lines.append("build: release")
        #endif
        lines.append(cpu(model: model, name: "tour, Standard", level: .standard, scenario: .tour))
        lines.append(cpu(model: model, name: "rocket at 1.6 R, Max", level: .max, scenario: HeroScenario(name: "redline", keys: [(0, 640)], duration: 12)))
        lines.append(contentsOf: gpu(model: model))
        lines.append(badge(model: model, name: "stairs, Cultivation, Standard", theme: .cultivation, level: .standard, scenario: .stairs))
        lines.append(badge(model: model, name: "stairs, Wuxia (two lines), Max", theme: .wuxia, level: .max, scenario: .stairs))
        lines.append(badge(model: model, name: "1.6 R, Engineer, Max", theme: .engineer, level: .max,
                           scenario: HeroScenario(name: "redline", keys: [(0, 640)], duration: 12)))
        let report = lines.joined(separator: "\n") + "\n"
        try? report.write(to: directory.appendingPathComponent("hero-bench.txt"), atomically: true, encoding: .utf8)
        print(report, terminator: "")
    }

    private static func cpu(model: AppModel, name: String, level: HeroLevel, scenario: HeroScenario) -> String {
        let timeline = HeroTimeline(model: model)
        timeline.hero?.overrides.level = level
        let start = timeline.time
        timeline.reset(value: scenario.value(at: 0))
        timeline.hero?.collectsSamples = true
        timeline.run(until: start + scenario.duration) { scenario.value(at: $0 - start) }
        let samples = (timeline.hero?.stats.samples ?? []).sorted()
        return "hero frame CPU (\(name), \(samples.count) frames): " + summary(samples)
    }

    /// CPU time of the title badge per frame (state step and scene build), DESIGN 4c.
    private static func badge(model: AppModel, name: String, theme: TitlePreset, level: HeroLevel, scenario: HeroScenario) -> String {
        let selected = model.titles.document.selected
        model.titles.select(theme.rawValue)
        defer { model.titles.select(selected) }
        let timeline = HeroTimeline(model: model)
        timeline.hero?.overrides.level = level
        let start = timeline.time
        timeline.reset(value: scenario.value(at: 0))
        timeline.animator.badge?.collectsSamples = true
        timeline.run(until: start + scenario.duration) { scenario.value(at: $0 - start) }
        let samples = (timeline.animator.badge?.samples ?? []).sorted()
        return "title badge frame CPU (\(name), \(samples.count) frames): " + summary(samples)
    }

    /// Encodes the layer of a busy frame (a rocket at Max with an upgrade in flight) offscreen and times it.
    private static func gpu(model: AppModel) -> [String] {
        guard let gpu = OrbGPU.shared, let heroGPU = gpu.hero else { return ["GPU: unavailable"] }
        let timeline = HeroTimeline(model: model)
        timeline.hero?.overrides.level = .max
        timeline.reset(value: 300)
        timeline.run(for: 1.5, at: 300)
        _ = timeline.runUntilTitleChange(at: 640)
        timeline.run(for: 0.2, at: 640)
        guard let hero = timeline.hero else { return [] }
        let geometry = OrbGeometry(diameter: HeroTimeline.orbDiameter, canvasSide: HeroTimeline.orbDiameter * OrbView.canvasScale)
        let layer = hero.layer(model: model, content: timeline.content(), geometry: geometry, time: timeline.time, sweepEpoch: 0)
        let side = HeroTimeline.orbDiameter * OrbView.canvasScale
        let pixels = Int(side * 2)
        let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: OrbGPU.pixelFormat, width: pixels, height: pixels, mipmapped: false)
        descriptor.usage = [.renderTarget]
        descriptor.storageMode = .private
        guard let texture = gpu.device.makeTexture(descriptor: descriptor) else { return ["GPU: no texture"] }
        let encoder = HeroLayerEncoder(gpu: heroGPU)
        var cpu: [UInt64] = []
        var gpuTimes: [UInt64] = []
        for _ in 0..<240 {
            let pass = MTLRenderPassDescriptor()
            pass.colorAttachments[0].texture = texture
            pass.colorAttachments[0].loadAction = .clear
            pass.colorAttachments[0].storeAction = .store
            heroGPU.configure(pass)
            guard let commands = gpu.queue.makeCommandBuffer(), let render = commands.makeRenderCommandEncoder(descriptor: pass) else { continue }
            let begin = DispatchTime.now().uptimeNanoseconds
            encoder.encode(layer.list, origin: layer.origin, unit: layer.unit, viewport: CGSize(width: side, height: side), scale: 2, into: render)
            cpu.append(DispatchTime.now().uptimeNanoseconds - begin)
            render.endEncoding()
            commands.commit()
            commands.waitUntilCompleted()
            gpuTimes.append(UInt64(max(0, commands.gpuEndTime - commands.gpuStartTime) * 1e9))
        }
        return [
            "busy frame: \(layer.list.items.count) items, \(layer.list.points.count) points",
            "layer encode CPU: " + summary(cpu.sorted()),
            "layer GPU (336 px, @2x): " + summary(gpuTimes.sorted()),
        ]
    }

    private static func summary(_ sorted: [UInt64]) -> String {
        guard !sorted.isEmpty else { return "no samples" }
        func at(_ p: Double) -> Double { Double(sorted[min(sorted.count - 1, Int(Double(sorted.count - 1) * p))]) / 1000 }
        let mean = Double(sorted.reduce(0, +)) / Double(sorted.count) / 1000
        return String(format: "mean %.1f µs, p50 %.1f, p95 %.1f, p99 %.1f, max %.1f", mean, at(0.5), at(0.95), at(0.99), at(1))
    }
}
