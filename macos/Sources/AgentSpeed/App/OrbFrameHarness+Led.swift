import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import Metal

/// A still of the v1.4 `led` orb in a state of the prototype's still-frame query (`spec/prototypes/led-effects.html`
/// `?v=…&t=6[&look=light][&level=1.6][&peak=…][&zap=1]`): R = 2000 tok/s, the value held for 6 s with the prototype's
/// response pulses, an optional new peak 0.25 s before the frame and lightning forced on every digit.
struct LedStill {
    var value: Double
    var size: CGFloat = 148
    var peak = false
    var zap = false
    /// Instead of a still: render 600 more frames offscreen and report the GPU time per frame.
    var gpu = false

    init?(_ parts: [String]) {
        guard parts.count > 1, let value = Double(parts[1]) else { return nil }
        self.value = value
        for part in parts.dropFirst(2) {
            if part == "peak" { peak = true }
            if part == "zap" { zap = true }
            if part == "gpu" { gpu = true }
            if part.hasPrefix("s"), let size = Double(part.dropFirst()) { self.size = CGFloat(size) }
        }
    }
}

extension OrbFrameHarness {
    static func renderLedStill(_ still: LedStill, clip: Clip, model: AppModel, to url: URL) {
        let previous = model.settings.redlineOutput
        model.settings.redlineOutput = 2000
        defer { model.settings.redlineOutput = previous }
        let stage = OrbPreviewStage(model: model, style: .led, diameter: still.size, dark: clip.dark, level: clip.level,
                                    reduceMotion: clip.reduceMotion)
        defer { stage.stop() }
        stage.reset(value: still.value)
        var random = OrbRandom(seed: 7)
        var nextPulse = 0.8
        let dt = 1.0 / 60
        let steps = 360
        for index in 0..<steps {
            if still.peak, index == steps - 15 { stage.animator.celebrate(at: stage.time) }
            if still.zap, index == steps - 1, let led = stage.animator.renderer as? LedStyle { led.zapEveryDigit() }
            if still.value > 2 {
                nextPulse -= dt
                if nextPulse <= 0 {
                    let heat = HeatPalette.heat(value: still.value, redline: 2000)
                    stage.animator.addPulse(at: stage.time, strength: 0.45 + random.next() * 0.55, heat: max(heat, 0.08))
                    nextPulse = 0.45 + random.next() * (1.4 - 0.9 * heat)
                }
            }
            stage.step(dt) { _ in still.value }
        }
        if still.gpu {
            print(gpuTiming(stage, value: still.value))
            return
        }
        let side = stage.canvasSide
        let background = clip.dark ? NSColor(srgbRed: 0x0B / 255, green: 0x0D / 255, blue: 0x15 / 255, alpha: 1)
            : NSColor(srgbRed: 0xE4 / 255, green: 0xE8 / 255, blue: 0xF0 / 255, alpha: 1)
        guard let image = stage.image(), let canvas = PreviewCanvas(size: CGSize(width: side, height: side), background: background) else {
            return
        }
        canvas.draw(image, at: .zero, size: CGSize(width: side, height: side))
        if let composed = canvas.image { PreviewSnapshot.write(composed, to: url) }
    }

    /// Mean GPU time per frame over 600 frames rendered offscreen at @2x and 72 fps (DESIGN 2a, GPU time per orb frame).
    private static func gpuTiming(_ stage: OrbPreviewStage, value: Double) -> String {
        guard let gpu = OrbGPU.shared else { return "gpu: no Metal device" }
        let pixels = Int((stage.canvasSide * 2).rounded())
        let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: OrbGPU.pixelFormat, width: pixels, height: pixels,
                                                                  mipmapped: false)
        descriptor.usage = [.renderTarget]
        descriptor.storageMode = .private
        guard let texture = gpu.device.makeTexture(descriptor: descriptor) else { return "gpu: no texture" }
        let pass = MTLRenderPassDescriptor()
        pass.colorAttachments[0].texture = texture
        pass.colorAttachments[0].loadAction = .clear
        pass.colorAttachments[0].storeAction = .store
        pass.colorAttachments[0].clearColor = MTLClearColor(red: 0, green: 0, blue: 0, alpha: 0)
        let encoder = OrbEncoder(gpu: gpu)
        let size = CGSize(width: stage.canvasSide, height: stage.canvasSide)
        var instances = 0
        // Paced like the 72 fps class of a 144 Hz display, so the GPU runs at the clocks it has on screen.
        for index in 0..<660 {
            stage.step(1.0 / 60) { _ in value }
            if index == 59 { _ = encoder.gpuTime.drain() }
            instances = max(instances, stage.lastScene.instanceCount)
            encoder.render(stage.lastScene, size: size, scale: 2, pass: pass, present: nil, waitUntilCompleted: true)
            Thread.sleep(forTimeInterval: 1.0 / 72)
        }
        encoder.render(stage.lastScene, size: size, scale: 2, pass: pass, present: nil, waitUntilCompleted: true)
        let (seconds, frames) = encoder.gpuTime.drain()
        return String(format: "gpu %.0f pt: %.3f ms per frame over %d frames (up to %d instances)", stage.diameter,
                      seconds / Double(max(1, frames)) * 1000, frames, instances)
    }
}
