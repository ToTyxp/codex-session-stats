using AgentSpeed.Core;
using AgentSpeed.Interop;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Settings.Orbs;
using AgentSpeed.Presentation.Updates;

namespace AgentSpeed.Services;

/// <summary>
/// Developer orb sessions (<c>--orb-demo</c>, <c>--orb-style</c>, <c>--orb-count</c>, <c>--orb-size</c>, <c>--theme</c>,
/// <c>--effects</c>): replaces the orbs for this session only, laid out in rows on the main display, so every style and
/// state and a multi-orb arrangement can be captured and measured. The <c>names*</c> demos place the orbs where each
/// name plate placement rule applies. Settings are not saved in such a session.
/// </summary>
internal static class OrbDemoSetup
{
    public static AppSettings Apply(AppSettings settings, LaunchArguments launch)
    {
        if (launch.Theme is { } theme)
        {
            settings = settings with { Appearance = theme == "light" ? AppearanceMode.Light : AppearanceMode.Dark };
        }

        if (launch.Effects is { } effects)
        {
            settings = settings with
            {
                Effects = effects switch
                {
                    "off" => EffectsIntensity.Off,
                    "soft" => EffectsIntensity.Soft,
                    "max" => EffectsIntensity.Max,
                    _ => EffectsIntensity.Standard,
                },
            };
        }

        if (launch.OrbDemo is { } demo && demo.StartsWith("names", StringComparison.Ordinal))
        {
            return NameDemo(settings, demo);
        }

        var style = OrbStyles.Parse(launch.OrbStyle);
        var size = launch.OrbSize ?? OrbDefinition.DefaultSize;
        var orbs = (launch.OrbDemo ?? (launch.OrbStyle is not null || launch.OrbCount is not null || launch.OrbSize is not null ? "grid" : null)) switch
        {
            "styles" => OrbStyles.All.Select(s => Orb(s, size, null)).ToList(),
            "multi" =>
            [
                Orb(OrbStyle.Led, 148, null),
                Orb(OrbStyle.Needle, 148, SourceSet.Of(SourceKind.Codex)),
                Orb(OrbStyle.Reactor, 112, SourceSet.Of(SourceKind.DeepSeek)),
                Orb(OrbStyle.Liquid, 148, SourceSet.Of(SourceKind.ClaudeCode).With(SourceKind.ClaudeDesktop)),
                Orb(OrbStyle.Minimal, 96, SourceSet.Of(SourceKind.Codex).With(SourceKind.DeepSeek)),
            ],
            "grid" => Enumerable.Range(0, launch.OrbCount ?? 1).Select(_ => Orb(style, size, null)).ToList(),
            _ => null,
        };

        if (orbs is null)
        {
            return settings;
        }

        var document = new OrbsDocument(orbs);
        var displays = Monitors.All().Select(m => new OrbDisplay(m.Key, m.WorkRect, m.Scale, m.IsPrimary)).ToArray();
        var main = displays.FirstOrDefault(d => d.IsMain);
        if (main.Key is not null)
        {
            var gap = 28 * main.Scale;
            var x = main.Work.X + 60 * main.Scale;
            var y = main.Work.Y + main.Work.Height * 0.3;
            var rowHeight = 0.0;
            foreach (var orb in orbs)
            {
                var radius = orb.Size / 2 * main.Scale;
                if (x + 2 * radius > main.Work.Right - 60 * main.Scale)
                {
                    x = main.Work.X + 60 * main.Scale;
                    y += rowHeight + gap;
                    rowHeight = 0;
                }

                document = OrbArrangement.Commit(document, displays, orb.Id, new PointD(x + radius, y + radius));
                x += 2 * radius + gap;
                rowHeight = Math.Max(rowHeight, 2 * radius);
            }
        }

        return settings with { Orbs = document, ShowOrbs = true };
    }

    private static OrbDefinition Orb(OrbStyle style, double size, SourceSet? sources) =>
        new OrbDefinition(OrbDefinition.NewId()) { Style = style, Sources = OrbSources.From(sources) }.WithSize(size);

    /// <summary>
    /// Name plate scenes on the main display (pt from its usable area, 12 pt margins and gaps):
    /// <c>names</c> — a top row (plates below), a row docked at the bottom edge (above) and a column docked at the right
    /// edge (on the left, like a legend); <c>names-cluster</c> — a 2 × 2 cluster (top pair above, bottom pair below), a
    /// 112 + 196 pair, a long custom name (ellipsis) and a 72 pt <c>minimal</c> (no plate); <c>names-crowd</c> — a
    /// 3 × 3 block without one corner, whose middle orb has no room for its plate.
    /// </summary>
    private static AppSettings NameDemo(AppSettings settings, string demo)
    {
        var displays = Monitors.All().Select(m => new OrbDisplay(m.Key, m.WorkRect, m.Scale, m.IsPrimary)).ToArray();
        var main = displays.FirstOrDefault(d => d.IsMain);
        if (main.Key is null)
        {
            return settings;
        }

        var w = main.Work.Width / main.Scale;
        var h = main.Work.Height / main.Scale;
        var placed = new List<(OrbDefinition Orb, double X, double Y)>();
        void Add(OrbStyle style, double size, double x, double y, string? name, params SourceKind[] sources) =>
            placed.Add((Orb(style, size, sources.Length == 0 ? null : SourceSet.Create(sources)) with { Name = name }, x, y));

        const double M = 12;
        switch (demo)
        {
            case "names-cluster":
                var cx = w / 2;
                var cy = h / 2;
                Add(OrbStyle.Minimal, 112, cx - 62, cy - 62, null, SourceKind.DeepSeek);
                Add(OrbStyle.Led, 112, cx + 62, cy - 62, null, SourceKind.OpenCode);
                Add(OrbStyle.Needle, 112, cx - 62, cy + 62, null, SourceKind.Omp);
                Add(OrbStyle.Liquid, 112, cx + 62, cy + 62, null, SourceKind.ClaudeDesktop);
                Add(OrbStyle.Reactor, 112, w * 0.18, h * 0.28, null, SourceKind.ClaudeCode, SourceKind.ClaudeDesktop);
                Add(OrbStyle.Led, 196, w * 0.18 + 56 + M + 98, h * 0.28, null, SourceKind.Codex, SourceKind.Gemini);
                Add(OrbStyle.Needle, 148, w * 0.78, h * 0.28, "Codex 夜間重構批次任務與相依套件升級檢查", SourceKind.Codex);
                Add(OrbStyle.Minimal, 72, w * 0.78, h * 0.75, null, SourceKind.Antigravity);
                break;
            case "names-crowd":
                for (var row = 0; row < 3; row++)
                {
                    for (var column = 0; column < 3; column++)
                    {
                        if (row != 0 || column != 0)
                        {
                            var kind = (SourceKind)((row * 3 + column) % 8);
                            Add(OrbStyles.All[(row + column) % OrbStyles.All.Count], 112, w / 2 + (column - 1) * 124, h / 2 + (row - 1) * 124, null, kind);
                        }
                    }
                }

                break;
            default:
                Add(OrbStyle.Led, 112, M + 56, M + 56, null, SourceKind.Codex);
                Add(OrbStyle.Needle, 112, M + 56 + 124, M + 56, null, SourceKind.ClaudeCode);
                Add(OrbStyle.Reactor, 112, M + 56 + 248, M + 56, null, SourceKind.Gemini, SourceKind.DeepSeek);
                Add(OrbStyle.Liquid, 148, M + 74, h - M - 74, null, SourceKind.ClaudeCode, SourceKind.ClaudeDesktop);
                Add(OrbStyle.Needle, 148, M + 74 + 160, h - M - 74, null);
                Add(OrbStyle.Liquid, 112, w - M - 56, M + 56, null, SourceKind.Codex);
                Add(OrbStyle.Led, 112, w - M - 56, M + 56 + 124, null, SourceKind.ClaudeCode, SourceKind.Codex, SourceKind.Gemini);
                Add(OrbStyle.Reactor, 112, w - M - 56, M + 56 + 248, "コードレビュー", SourceKind.Antigravity);
                break;
        }

        var document = new OrbsDocument(placed.Select(p => p.Orb));
        foreach (var (orb, x, y) in placed)
        {
            var center = new PointD(main.Work.X + x * main.Scale, main.Work.Y + y * main.Scale);
            document = OrbArrangement.Commit(document, displays, orb.Id, center);
        }

        return settings with { Orbs = document, ShowOrbs = true };
    }
}
