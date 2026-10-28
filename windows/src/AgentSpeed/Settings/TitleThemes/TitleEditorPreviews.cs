using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Core;
using AgentSpeed.Orb;
using AgentSpeed.Orb.Hero;
using AgentSpeed.Orb.Render;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Presentation.Titles;

namespace AgentSpeed.Settings.TitleThemes;

/// <summary>
/// What both editor previews show (DESIGN 5, Title themes editor): a level of the edited theme at 1.05 × its threshold
/// with Standard effects; a level change plays the real transition, a rename (typing) just redraws. They render only
/// while <see cref="Active"/>.
/// </summary>
internal abstract class TitlePreviewElement : FrameworkElement
{
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(nameof(Labels), typeof(IReadOnlyList<string>),
        typeof(TitlePreviewElement), new PropertyMetadata(null, OnLook));

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(int),
        typeof(TitlePreviewElement), new PropertyMetadata(TitleEditorMotion.RestingLevel, (d, _) => ((TitlePreviewElement)d).OnLevelChanged()));

    public static readonly DependencyProperty RedlineProperty = DependencyProperty.Register(nameof(Redline), typeof(double),
        typeof(TitlePreviewElement), new PropertyMetadata(HeatMath.DefaultRedline, OnLook));

    public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(nameof(Dark), typeof(bool),
        typeof(TitlePreviewElement), new PropertyMetadata(true, OnLook));

    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.Register(nameof(ReduceMotion), typeof(bool),
        typeof(TitlePreviewElement), new PropertyMetadata(false, OnLook));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string),
        typeof(TitlePreviewElement), new PropertyMetadata("tok/s", OnLook));

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool),
        typeof(TitlePreviewElement), new PropertyMetadata(false, (d, _) => ((TitlePreviewElement)d).OnActiveChanged()));

    /// <summary>Two agents generating, so the readout shows its agent dots as on a real orb.</summary>
    protected static readonly SourceKind[] Agents = [SourceKind.ClaudeCode, SourceKind.Codex];

    private bool _configureQueued;

    protected TitlePreviewElement()
    {
        Focusable = false;
        IsHitTestVisible = false;
        ClipToBounds = false;
        Loaded += (_, _) =>
        {
            Configure();
            Push(animated: false);
            OnActiveChanged();
        };
    }

    public IReadOnlyList<string>? Labels { get => (IReadOnlyList<string>?)GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }

    /// <summary>0 (idle) or 1–13.</summary>
    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    public double Redline { get => (double)GetValue(RedlineProperty); set => SetValue(RedlineProperty, value); }

    public bool Dark { get => (bool)GetValue(DarkProperty); set => SetValue(DarkProperty, value); }

    public bool ReduceMotion { get => (bool)GetValue(ReduceMotionProperty); set => SetValue(ReduceMotionProperty, value); }

    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    /// <summary>The preview can be seen (page shown, window not minimized, scrolled into view).</summary>
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    protected IReadOnlyList<string> LabelsOrEmpty => Labels ?? [];

    protected double Value => TitleEditorMotion.PreviewValue(Level, Redline);

    protected SpeedTier Tier => Level <= 0 ? SpeedTier.Idle : HeatMath.RawTier(Value, Redline);

    protected SpeedTitle Title => TitleSettingsText.LevelTitle(Level);

    protected abstract void Configure();

    protected abstract void Push(bool animated);

    protected abstract void OnActiveChanged();

    protected static void OnLook(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TitlePreviewElement)d).QueueConfigure();

    /// <summary>Several look properties change together (a refresh): configure once, then show the level again.</summary>
    private void QueueConfigure()
    {
        if (_configureQueued || !IsLoaded)
        {
            return;
        }

        _configureQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _configureQueued = false;
            Configure();
            Push(animated: false);
        });
    }

    private void OnLevelChanged()
    {
        if (IsLoaded)
        {
            Push(animated: true);
        }
    }
}

/// <summary>The floating orb preview at 96 DIP in a given style (the renderer the orb pane's previews use), ≤ 30 fps.</summary>
internal sealed class TitleOrbPreview : TitlePreviewElement
{
    public const double Diameter = 96;

    public static readonly DependencyProperty StyleKindProperty = DependencyProperty.Register(nameof(StyleKind), typeof(OrbStyle),
        typeof(TitleOrbPreview), new PropertyMetadata(OrbStyle.Led, OnLook));

    public static readonly DependencyProperty LatinProperty = DependencyProperty.Register(nameof(Latin), typeof(bool),
        typeof(TitleOrbPreview), new PropertyMetadata(false, OnLook));

    /// <summary>A level change plays at 30 fps; once it settled the orb idles along at 15 fps.</summary>
    private static readonly TimeSpan TransitionTime = TimeSpan.FromSeconds(OrbScene.PeakPillDuration);

    private readonly OrbSurface? _surface;
    private readonly DispatcherTimer _settle;

    public TitleOrbPreview()
    {
        if (OrbClock.Current is { } clock)
        {
            _surface = new OrbSurface(clock, (uint)Environment.TickCount | 1) { Cap = FrameClass.Fps15, IsHitTestVisible = false, Paused = true };
            AddVisualChild(_surface);
        }

        _settle = new DispatcherTimer(TransitionTime, DispatcherPriority.Background, (sender, _) =>
        {
            ((DispatcherTimer)sender!).Stop();
            if (_surface is not null)
            {
                _surface.Cap = FrameClass.Fps15;
            }
        }, Dispatcher);
        _settle.Stop();
        Unloaded += (_, _) => _settle.Stop();
    }

    public OrbStyle StyleKind { get => (OrbStyle)GetValue(StyleKindProperty); set => SetValue(StyleKindProperty, value); }

    /// <summary>English: the readout tracks its Latin titles.</summary>
    public bool Latin { get => (bool)GetValue(LatinProperty); set => SetValue(LatinProperty, value); }

    protected override int VisualChildrenCount => _surface is null ? 0 : 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 && _surface is not null ? _surface : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        _surface?.Measure(new Size(Diameter * 1.5, Diameter * 1.5));
        return new Size(Diameter, Diameter);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _surface?.Arrange(new Rect(-Diameter * 0.25, -Diameter * 0.25, Diameter * 1.5, Diameter * 1.5));
        return finalSize;
    }

    protected override void Configure()
    {
        if (_surface is null)
        {
            return;
        }

        var highContrast = SystemParameters.HighContrast;
        var effects = new EffectsPolicy(EffectsIntensity.Standard, ReduceMotion, highContrast);
        _surface.Configure(new OrbSurfaceConfig(StyleKind, Diameter, Dark ? OrbAppearance.Dark : OrbAppearance.Light, effects, highContrast,
            false, 1, false, false, Redline, new ReadoutTexts(Unit, string.Empty, LabelsOrEmpty, Latin), null, default));
    }

    protected override void Push(bool animated)
    {
        if (_surface is null)
        {
            return;
        }

        if (animated)
        {
            _surface.Cap = FrameClass.Fps30;
            _settle.Stop();
            _settle.Start();
        }

        _surface.SetData(Value, Tier, Title, Agents, string.Empty, null);
    }

    protected override void OnActiveChanged()
    {
        if (_surface is null)
        {
            return;
        }

        var paused = !Active || !IsLoaded;
        var resumed = _surface.Paused && !paused;
        _surface.Paused = paused;
        if (resumed)
        {
            // A paused surface keeps its layers as they were; configuring again redraws the readout for the current texts.
            Configure();
        }
    }
}

/// <summary>
/// The dashboard hero preview at 112 DIP, configured as the dashboard configures its hero: the ring at Soft, the vehicle
/// (Vehicles-based sets) or the title badge at Standard.
/// </summary>
internal sealed class TitleHeroPreview : TitlePreviewElement
{
    public const double Diameter = 112;

    public static readonly DependencyProperty ShowsVehicleProperty = DependencyProperty.Register(nameof(ShowsVehicle), typeof(bool),
        typeof(TitleHeroPreview), new PropertyMetadata(true, OnLook));

    public static readonly DependencyProperty LanguageCodeProperty = DependencyProperty.Register(nameof(LanguageCode), typeof(string),
        typeof(TitleHeroPreview), new PropertyMetadata(string.Empty, OnLook));

    public static readonly DependencyProperty PeakTextProperty = DependencyProperty.Register(nameof(PeakText), typeof(string),
        typeof(TitleHeroPreview), new PropertyMetadata(string.Empty, OnLook));

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private readonly OrbView _view;

    public TitleHeroPreview()
    {
        _view = new OrbView(() => Clock.Elapsed.TotalSeconds) { IsHitTestVisible = false, Paused = true };
        AddVisualChild(_view);
    }

    public bool ShowsVehicle { get => (bool)GetValue(ShowsVehicleProperty); set => SetValue(ShowsVehicleProperty, value); }

    public string LanguageCode { get => (string)GetValue(LanguageCodeProperty); set => SetValue(LanguageCodeProperty, value); }

    public string PeakText { get => (string)GetValue(PeakTextProperty); set => SetValue(PeakTextProperty, value); }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _view : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize)
    {
        _view.Measure(new Size(Diameter * 1.5, Diameter * 1.5));
        return new Size(Diameter, Diameter);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _view.Arrange(new Rect(-Diameter * 0.25, -Diameter * 0.25, Diameter * 1.5, Diameter * 1.5));
        return finalSize;
    }

    protected override void Configure()
    {
        var highContrast = SystemParameters.HighContrast;
        var labels = LabelsOrEmpty;
        var ring = new EffectsPolicy(EffectsIntensity.Standard, ReduceMotion, highContrast).CappedAt(EffectsIntensity.Soft);
        var hero = new HeroConfig(new HeroOptions(EffectsIntensity.Standard, ReduceMotion, highContrast, Dark), null, HeroGlowMode.Blur)
        {
            Titles = ShowsVehicle ? null : new HeroTitles(labels, LanguageCode),
        };
        _view.Configure(new OrbConfig(Diameter, Dark ? OrbAppearance.Dark : OrbAppearance.Light, 1, ring, highContrast, false, false, false,
            Redline, Unit, PeakText, labels) { Hero = hero });
    }

    protected override void Push(bool animated) => _view.SetData(Value, Tier, Title, Agents, string.Empty);

    protected override void OnActiveChanged() => _view.Paused = !Active || !IsLoaded;
}

/// <summary>A preview for UI Automation: one image labelled <c>titles.custom.preview</c> and the title it shows.</summary>
internal sealed class TitlePreviewImage : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new ImagePeer(this);

    /// <summary>Clips the orb's aura to the card's rounded corners.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        var radius = CornerRadius.TopLeft;
        var clip = new RectangleGeometry(new Rect(sizeInfo.NewSize), radius, radius);
        clip.Freeze();
        Clip = clip;
    }

    private sealed class ImagePeer(TitlePreviewImage owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override string GetClassNameCore() => nameof(TitlePreviewImage);
    }
}
