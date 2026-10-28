using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AgentSpeed.Localization;
using AgentSpeed.Theme;

namespace AgentSpeed.Reporting;

/// <summary>
/// <c>report.preview</c> (DESIGN 11, Preview window): the request line and headers — the signature as <c>…</c>, the API key as
/// <c>Bearer ••••</c> — then the body, the seconds until the next report and Copy. Before the service's document is known it
/// shows the body only, built with the default switches and the protocol's defaults, and says so.
/// </summary>
internal sealed partial class ReportPreviewWindow : Window
{
    private readonly ReportController _report;
    private readonly DispatcherTimer _clock;
    private readonly RichTextBox _preview;

    public ReportPreviewWindow(ReportController report)
    {
        InitializeComponent();
        _report = report;
        _preview = ReportPreviewText.Create(LocalizationSource.Instance["report.preview.title"]);
        PreviewHost.Child = _preview;
        if (AppIconSource.Frame(32) is { } icon)
        {
            Icon = icon;
        }

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        _report.OpenPreview();
        Loaded += (_, _) =>
        {
            Refresh();
            _clock.Start();
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _report.ClosePreview();
        };
    }

    public void Refresh()
    {
        var loc = LocalizationSource.Instance.Localizer;
        if (_report.Preview() is not { } preview)
        {
            return;
        }

        IReadOnlyList<(string, string)>? header = preview.Assumed
            ? null
            : [(string.Empty, preview.RequestLine), .. preview.Headers.Select(h => (h.Key, h.Value))];
        ReportPreviewText.Update(_preview, preview.Body.ToPrettyTokens(), header);
        HintText.Text = loc[preview.Assumed ? "report.preview.assumed" : "report.preview.hint"];
        if (preview.NextIn is { } seconds)
        {
            NextText.Text = loc.Format("report.preview.next", seconds);
            NextPill.Visibility = Visibility.Visible;
        }
        else
        {
            NextPill.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_report.Preview() is { } preview)
        {
            ReportLinks.Copy(preview.CopyText);
        }
    }
}
