using System.Globalization;
using System.IO;
using System.Text;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Titles;
using AgentSpeed.Titles;
using Xunit.Abstractions;

namespace AgentSpeed.Windows.Tests;

/// <summary>
/// DESIGN 1, Title length: every preset title in every language measured with the Windows fonts of the readout's title
/// style (Segoe UI Variable Text with Microsoft JhengHei UI / Microsoft YaHei UI / Yu Gothic UI, semibold, uppercase in
/// English) at the 7 pt floor must stay within the maximum budget, except the two Vehicles titles that predate it.
/// </summary>
public class TitleWidthTests(ITestOutputHelper output)
{
    private static readonly HashSet<(string Language, string Title)> Exempt = [(LanguageCodes.En, "MOTORCYCLE"), (LanguageCodes.En, "BULLET TRAIN")];

    [Fact]
    public void EveryPresetTitleFitsTheMaximumBudgetInEveryLanguage()
    {
        var rows = OnStaThread(Measure);
        var report = new StringBuilder("language\ttheme\tlevel\ttitle\tw7\tbudget\n");
        foreach (var row in rows)
        {
            var budget = row.Width <= TitleText.ComfortableWidth ? "comfortable" : row.Width <= TitleText.MaximumWidth ? "maximum" : "over";
            report.Append(CultureInfo.InvariantCulture, $"{row.Language}\t{row.Theme}\t{row.Level}\t{row.Title}\t{row.Width:0.0}\t{budget}\n");
        }

        var path = Path.Combine(AppContext.BaseDirectory, "title-widths.tsv");
        File.WriteAllText(path, report.ToString());
        output.WriteLine($"{rows.Count} titles measured; report: {path}");
        foreach (var row in rows.Where(r => r.Width > TitleText.ComfortableWidth))
        {
            output.WriteLine(FormattableString.Invariant($"{row.Language} {row.Theme} {row.Level} {row.Title}: {row.Width:0.0}"));
        }

        Assert.Equal(TitlePresets.All.Count * 14 * LanguageCodes.All.Count, rows.Count);
        var over = rows.Where(r => r.Width > TitleText.MaximumWidth && !Exempt.Contains((r.Language, r.Title))).ToArray();
        Assert.True(over.Length == 0, string.Join("; ", over.Select(r => FormattableString.Invariant($"{r.Language} {r.Theme} {r.Title} {r.Width:0.0}"))));
    }

    [Fact]
    public void TheCommitCutKeepsCustomTitlesWithinTheMaximumBudget()
    {
        var results = OnStaThread(() => LanguageCodes.All.Select(language =>
        {
            var text = TitleText.Sanitize("Supercalifragilistic expialidocious").Text;
            var fitted = TitleText.Fitted(text, TitleText.MaximumWidth, t => TitleMeasure.W7(t, language));
            return (language, fitted, width: TitleMeasure.W7(fitted.Text, language));
        }).ToArray());
        foreach (var (language, fitted, width) in results)
        {
            Assert.True(fitted.Cut, language);
            Assert.InRange(width, 30, TitleText.MaximumWidth);
            output.WriteLine(FormattableString.Invariant($"{language}: {fitted.Text} {width:0.0}"));
        }
    }

    private static List<(string Language, string Theme, int Level, string Title, double Width)> Measure()
    {
        var table = Localizer.LoadEmbeddedTable();
        var rows = new List<(string, string, int, string, double)>();
        foreach (var language in LanguageCodes.All)
        {
            var loc = new Localizer(table, language);
            foreach (var preset in TitlePresets.All)
            {
                var theme = TitleTheme.Of(preset);
                for (var level = 0; level <= TitlePresets.LevelCount; level++)
                {
                    var title = TitleText.DisplayCase(theme.Text(level, loc.Get), language == LanguageCodes.En);
                    rows.Add((language, TitlePresets.Id(preset), level, title, TitleMeasure.W7(title, language)));
                }
            }
        }

        return rows;
    }

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("Measuring failed.", error);
    }
}
