using System.IO;
using System.Text.Json;
using System.Windows;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Localization;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Theme;

namespace AgentSpeed.ProfileHarness;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var output = Option(args, "--output") ?? Path.Combine(Path.GetTempPath(), "agent-speed-profile-harness-" + Guid.NewGuid().ToString("N"));
        if (!Path.IsPathFullyQualified(output) || output.StartsWith(@"\\", StringComparison.Ordinal)
            || (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())) return 2;
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("AGENT_SPEED_SETTINGS_FILE", Path.Combine(output, "settings.json"));
        Environment.SetEnvironmentVariable("AGENT_SPEED_REPORT_DIR", Path.Combine(output, "keys"));
        Environment.SetEnvironmentVariable("AGENT_SPEED_LOGS_DIR", Path.Combine(output, "logs"));
        Environment.SetEnvironmentVariable("AGENT_SPEED_REPORT_LOOPBACK_ONLY", "1");
        var nativePassed = NativeTests.Run(output, Option(args, "--native-filter"));
        if (args.Contains("--no-ui", StringComparer.Ordinal)) return nativePassed ? 0 : 1;
        var service = ReportServiceUrl.Parse(Option(args, "--service") ?? "http://127.0.0.1:18789");
        if (service is null || !service.DiscoveryUrl.IsLoopback) return 2;
        var app = new AgentSpeed.App.App();
        var loc = new Localizer(Localizer.LoadEmbeddedTable(), Option(args, "--language") ?? "en");
        LocalizationSource.Instance.Attach(loc);
        using var theme = new ThemeService(app.Dispatcher, Presentation.Settings.AppearanceMode.Light, forceReduceMotion: true);
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AgentSpeed;component/Theme/Styles.xaml", UriKind.Absolute) });
        AppFonts.Apply(app.Resources, loc.LanguageCode);
        theme.EnableFluent();
        var hold = int.TryParse(Option(args, "--hold-seconds"), out var seconds) ? Math.Clamp(seconds, 0, 600) : 0;
        app.Startup += async (_, _) =>
        {
            var success = false;
            try { await ProfileFlow.Run(app, service, output, loc, hold); success = true; }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "ui-error.json"), JsonSerializer.Serialize(new { step = ProfileFlow.Step, error = error.GetBaseException().Message, type = error.GetBaseException().GetType().Name }));
            }
            finally { app.Shutdown(success && nativePassed ? 0 : 1); }
        };
        return app.Run();
    }
    private static string? Option(string[] args, string name)
    {
        var at = Array.IndexOf(args, name);
        return at >= 0 && at < args.Length - 1 ? args[at + 1] : null;
    }
}
