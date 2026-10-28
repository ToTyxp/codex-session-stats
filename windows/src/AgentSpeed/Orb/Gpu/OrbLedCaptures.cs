using System.Globalization;
using System.IO;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.Orb.Gpu.Styles;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Orb.Gpu;

/// <summary>
/// Developer harness <c>AGENT_SPEED_ORB_FRAMES=DIR</c> with <c>AGENT_SPEED_ORB_FRAMES_LED=1</c>: renders the floating <c>led</c> orb
/// in the states of the reference prototype's still frames (<c>spec/prototypes/led-effects.html?v=…&amp;t=6</c>): six simulated
/// seconds at 60 fps from a fresh orb, so the speed phases match the prototype's, at S = 148 and 240 and the pixel scale of
/// <c>AGENT_SPEED_ORB_FRAMES_SCALE</c> (default 2). One raw premultiplied BGRA image per state (<c>led-NAME-SIZE.bgra</c>), then exits.
/// </summary>
internal static class OrbLedCaptures
{
    private const double Redline = 2000;
    private const double Step = 1 / 60.0;

    private sealed record Shot(string Name, double Value, bool Dark = true, EffectsIntensity Level = EffectsIntensity.Standard,
        double PeakAt = -1, bool Zap = false, double PulseAt = -1, bool ReduceMotion = false);

    private static readonly Shot[] Shots =
    [
        new("idle", 0),
        new("120", 120),
        new("700", 700),
        new("1500", 1500),
        new("2600", 2600),
        new("2600-light", 2600, Dark: false),
        new("700-light", 700, Dark: false),
        new("peak", 1500, PeakAt: 5.5),
        new("peak-light", 1500, Dark: false, PeakAt: 5.5),
        new("zap", 1500, Zap: true),
        new("zap-light", 1500, Dark: false, Zap: true),
        new("pulse", 1500, PulseAt: 5.6),
        new("2600-off", 2600, Level: EffectsIntensity.Off),
        new("2600-soft", 2600, Level: EffectsIntensity.Soft),
        new("2600-max", 2600, Level: EffectsIntensity.Max),
        new("2600-reduce", 2600, ReduceMotion: true),
    ];

    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!GpuDevice.Initialize(composition: false) || GpuDevice.Shared is not { } device)
        {
            File.WriteAllText(Path.Combine(directory, "led-frames.txt"), "Direct3D 11 unavailable: " + GpuDevice.Failure);
            return 2;
        }

        var scale = double.TryParse(Environment.GetEnvironmentVariable("AGENT_SPEED_ORB_FRAMES_SCALE"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 2;
        var only = Environment.GetEnvironmentVariable("AGENT_SPEED_ORB_FRAMES_LED") is { Length: > 1 } list
            ? list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), LanguageCodes.ZhHant);
        device.Atlas.Culture = loc.Culture;
        var titles = Enum.GetValues<SpeedTitle>().Select(t => DisplayText.TitleLabel(t, loc)).ToArray();
        var strings = new OrbReadoutStrings(loc["unit.tokS"], loc["peak.new"], titles, loc.UsesLatinScript);
        var report = new System.Text.StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"adapter: {device.AdapterName}, scale {scale}");
        using var target = new OffscreenTarget();
        var sizes = (Environment.GetEnvironmentVariable("AGENT_SPEED_ORB_FRAMES_SIZES") ?? "148,240")
            .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        foreach (var size in sizes)
        {
            foreach (var shot in Shots)
            {
                if (only is not null && !only.Contains(shot.Name))
                {
                    continue;
                }

                var pixels = (int)Math.Round(size * 1.5 * scale);
                var bytes = Render(device, target, shot, size, scale, strings, pixels, out var count);
                if (bytes is null)
                {
                    report.AppendLine(CultureInfo.InvariantCulture, $"{shot.Name} {size}: device lost");
                    continue;
                }

                // Premultiplied BGRA as rendered: additive light keeps more color than alpha, which a PNG cannot hold.
                File.WriteAllBytes(Path.Combine(directory, $"led-{shot.Name}-{size:0}.bgra"), bytes);

                report.AppendLine(CultureInfo.InvariantCulture, $"{shot.Name} {size}: {count} instances");
            }
        }

        File.WriteAllText(Path.Combine(directory, "led-frames.txt"), report.ToString());
        return 0;
    }

    private static byte[]? Render(GpuDevice device, OffscreenTarget target, Shot shot, double size, double scale, OrbReadoutStrings strings,
        int pixels, out int count)
    {
        var effects = new EffectsPolicy(shot.Level, shot.ReduceMotion, false);
        var orb = new OrbGpuOrb(0x9E3779B9u, device.Atlas.ForScale(scale));
        var tokens = OrbTokens.For(shot.Dark ? OrbAppearance.Dark : OrbAppearance.Light);
        orb.Configure(new OrbGpuConfig(OrbStyle.Led, size, effects, true, true, Redline, strings, null, SourceSet.Empty),
            new OrbGpuContext(size, scale, tokens, effects));
        var time = 100.0;
        orb.SetData(time, shot.Value, HeatMath.RawTier(shot.Value, Redline), SpeedTitles.Raw(shot.Value, Redline), [], string.Empty, null);
        orb.SnapNext();
        device.Atlas.BeginFrame();
        orb.Render(time);
        for (var k = 1; k <= 360; k++)
        {
            if (shot.PeakAt >= 0 && k == (int)Math.Round(shot.PeakAt * 60))
            {
                orb.Peak(time);
            }

            if (shot.PulseAt >= 0 && k == (int)Math.Round(shot.PulseAt * 60))
            {
                orb.Pulse(time, shot.Value, 4000);
            }

            time += Step;
            device.Atlas.BeginFrame();
            orb.Render(time);
        }

        if (shot.Zap && orb.Style is LedDraw led)
        {
            led.Effects.ForceLightning();
            time += Step;
            device.Atlas.BeginFrame();
            orb.Render(time);
            time += Step;
            device.Atlas.BeginFrame();
            orb.Render(time);
        }

        byte[]? bytes = null;
        for (var i = 0; i < 3; i++)
        {
            bytes = target.Render(device, orb.List, pixels, pixels);
            if (!device.Atlas.NeedsRebuild)
            {
                break;
            }

            device.Atlas.BeginFrame();
            orb.Render(time);
        }

        count = orb.List.Count;
        return bytes;
    }
}
