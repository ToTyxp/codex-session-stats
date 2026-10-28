using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Dashboard;

/// <summary>
/// The dashboard's one-time invitation to live reporting (DESIGN 11, Dashboard invitation): the preset's name and domain, the
/// public name, a preview link and No Thanks / Turn On. It enters like the update banner; accepting cross-fades to one done
/// line held 2.4 s before the card collapses; declining or closing collapses it at once. With Reduce Motion nothing slides.
/// No button is the default, so consent never comes from a stray key.
/// </summary>
internal sealed partial class ReportInvitationCard
{
    private static readonly Duration EnterDuration = TimeSpan.FromMilliseconds(320);
    private static readonly Duration FadeDuration = TimeSpan.FromMilliseconds(200);
    private static readonly IEasingFunction Spring = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private bool _reduceMotion;
    private bool _answered;

    public ReportInvitationCard()
    {
        InitializeComponent();
    }

    public event EventHandler? Accepted;

    public event EventHandler? Declined;

    public event EventHandler? PreviewRequested;

    /// <summary>The card has finished collapsing after an answer.</summary>
    public event EventHandler? Collapsed;

    /// <summary>The texts as the app knows them (nothing is fetched): the preset's name and domain, and the public name.</summary>
    public void SetText(Localizer loc, string name, string domain, string username)
    {
        Body.Text = loc.Format("report.invite.body", name, domain);
        var template = loc["report.invite.username"];
        var marker = template.IndexOf("%@", StringComparison.Ordinal);
        UsernameLine.Inlines.Clear();
        var isolated = "\u2068" + username + "\u2069";
        if (marker < 0)
        {
            UsernameLine.Inlines.Add(new Run(loc.Format("report.invite.username", isolated)));
        }
        else
        {
            UsernameLine.Inlines.Add(new Run(template[..marker]));
            var run = new Run(isolated) { FontWeight = FontWeights.Medium };
            run.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            UsernameLine.Inlines.Add(run);
            UsernameLine.Inlines.Add(new Run(template[(marker + 2)..]));
        }

        AutomationProperties.SetHelpText(Shell, Body.Text + " " + loc.Format("report.invite.username", username));
    }

    /// <summary>The accent tint: 8 % (light) or 14 % (dark) over the panel, a 0.5 DIP hairline at 20 % (40 % with high contrast).</summary>
    public void SetAppearance(bool dark, bool highContrast, bool reduceMotion)
    {
        _reduceMotion = reduceMotion;
        var accent = TryFindResource("SystemAccentColor") is Color color ? color : Color.FromRgb(0x00, 0x78, 0xD4);
        Shell.Background = highContrast ? Brushes.Transparent : Tinted(accent, dark ? 0.14 : 0.08);
        Shell.BorderBrush = highContrast ? (TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.Gray) : Tinted(accent, dark ? 0.28 : 0.2);
        Shell.BorderThickness = new Thickness(highContrast ? 1 : 0.5);
        Body.SetResourceReference(TextBlock.ForegroundProperty, highContrast ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
    }

    private static SolidColorBrush Tinted(Color color, double opacity)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    /// <summary>Opacity and a short move from the top, with the dashboard's spring; a plain fade with Reduce Motion.</summary>
    public void Enter()
    {
        _answered = false;
        Offer.Visibility = Visibility.Visible;
        Offer.Opacity = 1;
        Done.Visibility = Visibility.Collapsed;
        Done.Opacity = 0;
        Height = double.NaN;
        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, _reduceMotion ? FadeDuration : EnterDuration) { EasingFunction = EaseOut });
        if (!_reduceMotion)
        {
            Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, EnterDuration) { EasingFunction = Spring });
        }
    }

    /// <summary>Hides the card at once (another card shows, or the invitation no longer applies).</summary>
    public void HideNow()
    {
        BeginAnimation(OpacityProperty, null);
        BeginAnimation(HeightProperty, null);
        Visibility = Visibility.Collapsed;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        Accepted?.Invoke(this, EventArgs.Empty);
        var height = ActualHeight;
        Offer.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, FadeDuration));
        Done.Visibility = Visibility.Visible;
        Done.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration) { BeginTime = TimeSpan.FromMilliseconds(80) });
        var doneHeight = Math.Max(40, Done.DesiredSize.Height + 22);
        if (!_reduceMotion)
        {
            BeginAnimation(HeightProperty, new DoubleAnimation(height, doneHeight + Shell.Margin.Bottom, EnterDuration) { EasingFunction = EaseOut });
        }

        var hold = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80 + 2400) };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            Offer.Visibility = Visibility.Collapsed;
            Collapse();
        };
        hold.Start();
        if (UIElementAutomationPeer.CreatePeerForElement(DoneText) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void OnDecline(object sender, RoutedEventArgs e)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        Declined?.Invoke(this, EventArgs.Empty);
        Collapse();
    }

    private void OnPreview(object sender, RoutedEventArgs e) => PreviewRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Height and opacity to zero (the opacity alone with Reduce Motion), then hidden.</summary>
    private void Collapse()
    {
        var fade = new DoubleAnimation(Opacity, 0, _reduceMotion ? FadeDuration : EnterDuration) { EasingFunction = EaseOut };
        fade.Completed += (_, _) =>
        {
            HideNow();
            Collapsed?.Invoke(this, EventArgs.Empty);
        };
        if (!_reduceMotion)
        {
            BeginAnimation(HeightProperty, new DoubleAnimation(ActualHeight, 0, EnterDuration) { EasingFunction = EaseOut });
        }

        BeginAnimation(OpacityProperty, fade);
    }
}
