using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.Settings;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace AgentSpeed.Orb.Gpu;

/// <summary>
/// Developer harness <c>AGENT_SPEED_ORB_BENCH=FILE</c>: the DESIGN 2a budget scenarios on the Direct3D renderer without a window.
/// Orbs run in real time on a simulated 144 Hz display clock, each rendering only on its frame class's ticks (crowding rule
/// included) into its own offscreen target; per scenario it reports the process CPU (% of one core over the window), the frames
/// per second, the CPU of a frame's state update and draw list, and the GPU time per frame. Presenting through the composition
/// swap chain is not included (the frame counts are). <c>AGENT_SPEED_ORB_BENCH_STYLES</c> picks the styles,
/// <c>AGENT_SPEED_ORB_BENCH_SCALE</c> the pixel scale (default 2.4, the reference machine).
/// </summary>
internal static class OrbBenchHarness
{
    private const double Redline = 2000;
    private const double RefreshHz = 144;

    private sealed record Scenario(string Name, int Count, double Size, double Value, EffectsIntensity Level);

    private static readonly Scenario[] Scenarios =
    [
        new("idle", 1, 148, 0, EffectsIntensity.Standard),
        new("cruise", 1, 148, 120, EffectsIntensity.Standard),
        new("redline", 1, 148, 3000, EffectsIntensity.Standard),
        new("redline max", 1, 148, 3000, EffectsIntensity.Max),
        new("redline S240", 1, 240, 3000, EffectsIntensity.Standard),
        new("redline S240 max", 1, 240, 3000, EffectsIntensity.Max),
        new("8 orbs redline", 8, 148, 3000, EffectsIntensity.Standard),
    ];

    public static int Run(string path)
    {
        if (!GpuDevice.Initialize(composition: false) || GpuDevice.Shared is not { } device)
        {
            File.WriteAllText(path, "Direct3D 11 unavailable: " + GpuDevice.Failure);
            return 2;
        }

        var scale = double.TryParse(Environment.GetEnvironmentVariable("AGENT_SPEED_ORB_BENCH_SCALE"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 2.4;
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("AGENT_SPEED_ORB_BENCH_SECONDS"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var w) && w > 0 ? w : 10;
        var styles = (Environment.GetEnvironmentVariable("AGENT_SPEED_ORB_BENCH_STYLES") ?? "led")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(OrbStyles.Parse).Distinct().ToArray();
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        device.Atlas.Culture = loc.Culture;
        var titles = Enum.GetValues<SpeedTitle>().Select(t => DisplayText.TitleLabel(t, loc)).ToArray();
        var strings = new OrbReadoutStrings(loc["unit.tokS"], loc["peak.new"], titles, loc.UsesLatinScript);
        OrbGpuRenderer.Timer = new GpuTimer();
        var report = new System.Text.StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"adapter: {device.AdapterName}, scale {scale}, {RefreshHz} Hz, {seconds} s per scenario");
        _ = NativeMethods.timeBeginPeriod(1);
        try
        {
            foreach (var style in styles)
            {
                // Untimed: tiered JIT, the glyph atlas and the driver settle before the first scenario.
                _ = Measure(device, style, new Scenario("warm-up", 2, 148, 3000, EffectsIntensity.Max), scale, 6, strings);
                foreach (var scenario in Scenarios)
                {
                    var line = Measure(device, style, scenario, scale, seconds, strings);
                    report.AppendLine(CultureInfo.InvariantCulture, $"{OrbStyles.Key(style)} {scenario.Name}: {line}");
                    File.WriteAllText(path, report.ToString());
                }
            }
        }
        finally
        {
            _ = NativeMethods.timeEndPeriod(1);
        }

        return 0;
    }

    private static string Measure(GpuDevice device, OrbStyle style, Scenario scenario, double scale, double seconds, OrbReadoutStrings strings)
    {
        var effects = new EffectsPolicy(scenario.Level);
        var tier = HeatMath.RawTier(scenario.Value, Redline);
        var title = SpeedTitles.Raw(scenario.Value, Redline);
        SourceKind[] agents = scenario.Value > 0 ? [SourceKind.Codex, SourceKind.ClaudeCode] : [];
        var pixels = (int)Math.Round(scenario.Size * 1.5 * scale);
        var orbs = new OrbGpuOrb[scenario.Count];
        var targets = new Target[scenario.Count];
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < orbs.Length; i++)
        {
            var orb = new OrbGpuOrb((uint)(0x9E3779B9u + i * 7919), device.Atlas.ForScale(scale));
            orb.Configure(new OrbGpuConfig(style, scenario.Size, effects, true, true, Redline, strings, null, SourceSet.Empty),
                new OrbGpuContext(scenario.Size, scale, OrbTokens.Dark, effects));
            orb.SetData(1000 + clock.Elapsed.TotalSeconds, scenario.Value, tier, title, agents, "today", null);
            orb.SnapNext();
            orbs[i] = orb;
            targets[i] = new Target(device, pixels);
        }

        var tsc = TscFrequency();
        var period = 1 / RefreshHz;
        var warm = 2.0;
        var measuring = false;
        var cyclesStart = 0UL;
        var wallStart = 0.0;
        var frames = 0;
        var buildTicks = 0L;
        var next = new long[orbs.Length];
        var demand = new FrameClass[orbs.Length];
        Array.Fill(demand, FrameClass.Fps60);
        while (true)
        {
            var elapsed = clock.Elapsed.TotalSeconds;
            var now = 1000 + elapsed;
            var tick = (long)(elapsed / period);
            if (!measuring && elapsed >= warm)
            {
                measuring = true;
                cyclesStart = ProcessCycles();
                wallStart = elapsed;
                frames = 0;
                buildTicks = 0;
                OrbGpuRenderer.Timer?.Drain();
            }

            if (measuring && elapsed - wallStart >= seconds)
            {
                break;
            }

            var sixty = 0;
            foreach (var d in demand)
            {
                sixty += d == FrameClass.Fps60 ? 1 : 0;
            }

            var rendered = false;
            var due = long.MaxValue;
            for (var i = 0; i < orbs.Length; i++)
            {
                var frameClass = FramePolicy.Crowded(demand[i], sixty, i == 0);
                if (frameClass == FrameClass.None && orbs[i].NextWake is { } wake)
                {
                    if (now < wake)
                    {
                        due = Math.Min(due, (long)Math.Ceiling((wake - 1000) / period));
                        continue;
                    }

                    frameClass = FrameClass.Fps30;
                }

                var divisor = FramePolicy.Divisor(frameClass, RefreshHz);
                if (divisor == 0)
                {
                    continue;
                }

                if (tick < next[i])
                {
                    due = Math.Min(due, next[i]);
                    continue;
                }

                next[i] = next[i] + divisor > tick ? next[i] + divisor : tick + divisor;
                due = Math.Min(due, next[i]);
                var start = Stopwatch.GetTimestamp();
                device.Atlas.BeginFrame();
                orbs[i].Render(now);
                buildTicks += Stopwatch.GetTimestamp() - start;
                targets[i].Draw(device, orbs[i].List, pixels);
                demand[i] = orbs[i].Demand;
                frames++;
                rendered = true;
            }

            if (rendered)
            {
                device.Current.Context.Flush();
            }

            var wait = Math.Min(due == long.MaxValue ? 0.05 : due * period, wallStart + seconds + warm) - clock.Elapsed.TotalSeconds;
            Thread.Sleep(Math.Max(1, (int)Math.Floor(Math.Min(wait, 0.05) * 1000)));
        }

        var wall = clock.Elapsed.TotalSeconds - wallStart;
        var cpu = (ProcessCycles() - cyclesStart) / tsc / wall * 100;
        var gpu = OrbGpuRenderer.Timer?.Drain() ?? (0.0, 0);
        foreach (var target in targets)
        {
            target.Dispose();
        }

        var perFrame = frames > 0 ? buildTicks * 1000.0 / Stopwatch.Frequency / frames : 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"cpu {cpu:0.00}% of one core (tsc {tsc / 1e9:0.00} GHz), {frames / wall:0.0} frames/s, build {perFrame:0.000} ms/frame, gpu {gpu.Milliseconds:0.000} ms/frame ({gpu.Frames} timed), {orbs[0].List.Count} instances");
    }

    /// <summary>CPU cycles of every thread of the process (exact, unlike the tick-sampled processor time).</summary>
    private static ulong ProcessCycles() =>
        NativeMethods.QueryProcessCycleTime(NativeMethods.GetCurrentProcess(), out var cycles) ? cycles : 0;

    /// <summary>The cycle counter's rate: a thread spinning for 250 ms.</summary>
    private static double TscFrequency()
    {
        var thread = NativeMethods.GetCurrentThread();
        NativeMethods.QueryThreadCycleTime(thread, out var c0);
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalMilliseconds < 250)
        {
        }

        NativeMethods.QueryThreadCycleTime(thread, out var c1);
        return (c1 - c0) / watch.Elapsed.TotalSeconds;
    }

    /// <summary>An offscreen render target without readback.</summary>
    private sealed class Target : IDisposable
    {
        private readonly ID3D11Texture2D _texture;
        private readonly ID3D11RenderTargetView _view;

        public Target(GpuDevice device, int pixels)
        {
            var r = device.Current;
            _texture = r.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)pixels, (uint)pixels, 1, 1,
                BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
            _view = r.Device.CreateRenderTargetView(_texture);
        }

        public void Draw(GpuDevice device, OrbDrawList list, int pixels) => OrbGpuRenderer.Draw(device, list, _view, pixels, pixels);

        public void Dispose()
        {
            _view.Dispose();
            _texture.Dispose();
        }
    }

    private static class NativeMethods
    {
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint period);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint period);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryProcessCycleTime(IntPtr process, out ulong cycles);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetCurrentThread();
    }
}
