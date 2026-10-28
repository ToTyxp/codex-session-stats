using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AgentSpeed.Localization;
using AgentSpeed.Theme;

namespace AgentSpeed.Reporting;

/// <summary>
/// The consent sheet as a Fluent dialog (DESIGN 11, Consent sheet): opened by the controller when reporting is turned on
/// for a service that is not confirmed yet, or by <c>report.service.review</c>; closed by Start's success or by Cancel.
/// Esc cancels, Return starts when the form is valid.
/// </summary>
internal sealed partial class ReportConsentDialog : Window
{
    private readonly ReportController _report;
    private readonly ReportConsentViewModel _model;
    private readonly DispatcherTimer _clock;
    private readonly RichTextBox _preview;
    private bool _closedByController;
    private bool _closing;

    public ReportConsentDialog(ReportController report, ReportConsentViewModel model)
    {
        InitializeComponent();
        _report = report;
        _model = model;
        DataContext = model;
        _preview = ReportPreviewText.Create(LocalizationSource.Instance["report.preview.title"]);
        PreviewHost.Child = _preview;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        if (AppIconSource.Frame(32) is { } icon)
        {
            Icon = icon;
        }

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshPreview(), Dispatcher);
        _report.Changed += OnReportChanged;
        _report.OpenPreview();
        Loaded += (_, _) =>
        {
            RefreshPreview();
            _clock.Start();
            UsernameBox.Focus();
            UsernameBox.SelectAll();
        };
        Closing += (_, _) =>
        {
            _closing = true;
            if (!_closedByController && _report.Consent is not null)
            {
                _report.CancelConsent();
            }
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _report.Changed -= OnReportChanged;
            _report.ClosePreview();
        };
    }

    /// <summary>The controller finished or dropped the consent: closes without cancelling it.</summary>
    public void CloseFromController()
    {
        _closedByController = true;
        if (!_closing)
        {
            Close();
        }
    }

    private void OnReportChanged(object? sender, EventArgs e)
    {
        if (_report.Consent is null)
        {
            CloseFromController();
            return;
        }

        _model.Refresh();
    }

    private void RefreshPreview()
    {
        if (_report.Preview(_report.Consent) is { } preview)
        {
            ReportPreviewText.Update(_preview, preview.Body.ToPrettyTokens());
        }
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e)
    {
        ApiKeyPlaceholder.Visibility = ApiKeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _model.SetApiKey(ApiKeyBox.Password);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_report.Preview(_report.Consent) is { } preview)
        {
            ReportLinks.Copy(preview.CopyText);
        }
    }
}
