using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AgentSpeed.Presentation.Titles;

namespace AgentSpeed.Dashboard;

/// <summary>
/// The hero's speed title (DESIGN 4.1): a new level slides the old title up and out over the first 40 % of 0.35 s and
/// the new one up into place from 35 %, so the two never overlap legibly; without motion (Reduce Motion, effects Off)
/// the same stagger runs as a fade. A new text on the same level (a theme or language change) crossfades in 0.25 s.
/// </summary>
internal sealed class TitleSwapLabel : Grid
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string),
        typeof(TitleSwapLabel), new PropertyMetadata(string.Empty, (d, _) => ((TitleSwapLabel)d).OnContentChanged()));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(int),
        typeof(TitleSwapLabel), new PropertyMetadata(0, (d, _) => ((TitleSwapLabel)d).OnContentChanged()));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush),
        typeof(TitleSwapLabel), new PropertyMetadata(Brushes.Gray, (d, _) => ((TitleSwapLabel)d).OnForegroundChanged()));

    public static readonly DependencyProperty SlidesProperty = DependencyProperty.Register(nameof(Slides), typeof(bool),
        typeof(TitleSwapLabel), new PropertyMetadata(true));

    private readonly TextBlock _incoming = Line();
    private readonly TextBlock _outgoing = Line();
    private readonly TranslateTransform _incomingShift = new();
    private readonly TranslateTransform _outgoingShift = new();
    private string _shownText = string.Empty;
    private int _shownLevel = -1;
    private Brush? _shownBrush;
    private bool _rename;
    private bool _rendering;
    private bool _pending;
    private TimeSpan? _start;

    public TitleSwapLabel()
    {
        ClipToBounds = true;
        _incoming.RenderTransform = _incomingShift;
        _outgoing.RenderTransform = _outgoingShift;
        _outgoing.Opacity = 0;
        _outgoing.IsHitTestVisible = false;
        Children.Add(_outgoing);
        Children.Add(_incoming);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                Finish();
            }
        };
        Unloaded += (_, _) => Finish();
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Titles slide (motion allowed); otherwise they only fade, keeping the stagger.</summary>
    public bool Slides { get => (bool)GetValue(SlidesProperty); set => SetValue(SlidesProperty, value); }

    private static TextBlock Line() => new()
    {
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private void OnForegroundChanged()
    {
        _incoming.Foreground = Foreground;
        if (_start is null)
        {
            _shownBrush = Foreground;
        }
    }

    /// <summary>Text and level arrive as two bindings; both are read once they have settled.</summary>
    private void OnContentChanged()
    {
        if (_pending)
        {
            return;
        }

        _pending = true;
        Dispatcher.BeginInvoke(Evaluate, System.Windows.Threading.DispatcherPriority.Normal);
    }

    private void Evaluate()
    {
        _pending = false;
        var text = Text ?? string.Empty;
        var level = Level;
        if (text == _shownText && level == _shownLevel)
        {
            return;
        }

        var animate = _shownLevel >= 0 && IsVisible && IsLoaded && text != _shownText;
        if (animate)
        {
            _outgoing.Text = _shownText;
            _outgoing.Foreground = _shownBrush ?? Foreground;
            _rename = level == _shownLevel;
            _start = null;
            StartRendering();
        }
        else
        {
            Finish();
        }

        _incoming.Text = text;
        _incoming.Foreground = Foreground;
        _shownText = text;
        _shownLevel = level;
        _shownBrush = Foreground;
        Apply(animate ? 0 : 1);
    }

    private void StartRendering()
    {
        if (!_rendering)
        {
            _rendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        _start ??= now;
        var elapsed = (now - _start.Value).TotalSeconds;
        var duration = _rename ? TitleTransitions.RenameDuration : TitleTransitions.SlideDuration;
        var p = Math.Clamp(elapsed / duration, 0, 1);
        Apply(p);
        if (p >= 1)
        {
            Finish();
        }
    }

    /// <summary>Places both lines at progress <paramref name="p"/> of the running transition.</summary>
    private void Apply(double p)
    {
        if (p >= 1)
        {
            _outgoing.Opacity = 0;
            _incoming.Opacity = 1;
            _incomingShift.Y = 0;
            _outgoingShift.Y = 0;
            return;
        }

        if (_rename)
        {
            var mix = TitleTransitions.Rename(p * TitleTransitions.RenameDuration);
            _outgoing.Opacity = 1 - mix;
            _incoming.Opacity = mix;
            _incomingShift.Y = 0;
            _outgoingShift.Y = 0;
            return;
        }

        var travel = Slides ? Math.Max(ActualHeight, 16) : 0;
        var outgoing = TitleTransitions.Outgoing(p);
        var incoming = TitleTransitions.Incoming(p);
        _outgoing.Opacity = outgoing.Alpha;
        _outgoingShift.Y = -travel * outgoing.Travel;
        _incoming.Opacity = incoming.Alpha;
        _incomingShift.Y = travel * (1 - incoming.Travel);
    }

    private void Finish()
    {
        if (_rendering)
        {
            CompositionTarget.Rendering -= OnRendering;
            _rendering = false;
        }

        _start = null;
        _outgoing.Text = string.Empty;
        Apply(1);
    }
}
