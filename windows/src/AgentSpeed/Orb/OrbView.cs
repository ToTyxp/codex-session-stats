using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.App;
using AgentSpeed.Core;
using AgentSpeed.Orb.Hero;
using AgentSpeed.Orb.Hero.Badge;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Gauge;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Hero.Badge;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Particles;
using AgentSpeed.Theme;

namespace AgentSpeed.Orb;

/// <summary>
/// The orb instrument (DESIGN section 2) drawn with retained DrawingVisual layers. Only layers that change are
/// re-recorded; frames run only while the scene animates, and never while the view is hidden. The dashboard hero
/// (embedded style) draws the hero vehicle or, for every title theme but Vehicles, the title badge (DESIGN 4c) in place
/// of the readout.
/// </summary>
internal sealed class OrbView : FrameworkElement
{
    private static readonly Typeface ValueFace = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Typeface UnitFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft YaHei UI, Yu Gothic UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _aura = new();
    private readonly DrawingVisual _body = new();
    private readonly DrawingVisual _gauge = new();
    private readonly DrawingVisual _comet = new();
    private readonly DrawingVisual _particles = new();
    private readonly DrawingVisual _readout = new();
    private readonly DrawingVisual _overlay = new();
    private readonly ScaleTransform _hoverScale = new(1, 1);
    private readonly TextCache _text = new();
    private readonly FrameDriver _driver;
    private readonly Func<double> _clock;
    private readonly DispatcherTimer _accessibilityTimer;

    private OrbConfig _config = OrbConfig.Default;
    private OrbTokens _tokens = OrbTokens.Dark;
    private OrbBrushes _brushes;
    private CriticalSpring _valueWidth = new(0.25);
    private string _infoLine = string.Empty;
    private string _accessibleName = string.Empty;
    private string _pendingAccessibleName = string.Empty;
    private bool _bodyDirty = true;
    private bool _hasDrawnStatic;
    private bool _isActive = true;
    private double _lastReadoutTime;
    private bool _hovering;
    private SpeedTier _lastTier = (SpeedTier)(-1);
    private SpeedTitle _lastTitle = (SpeedTitle)(-1);
    private string _lastText = string.Empty;
    private double _lastValue = double.NaN;
    private SourceKind[] _lastAgents = [];
    private long _auraKey = long.MinValue;
    private long _gaugeKey = long.MinValue;
    private long _readoutKey = long.MinValue;
    private bool _cometEmpty;
    private bool _particlesEmpty;
    private bool _overlayEmpty;
    private double _lastScale = double.NaN;
    private double _valueWidthTarget;
    private HeroLayer? _heroLayer;
    private HeroStage? _heroStage;
    private HeroConfig? _heroConfig;
    private readonly HeroStats _heroStats = new();
    private double _heroValue;
    private SpeedTier _heroTier = SpeedTier.Idle;
    private SpeedTitle _heroTitle = SpeedTitle.Idle;
    private double _heroResyncUntil = double.NegativeInfinity;
    private bool _heroSettledDrawn;
    private double _heroLastRender = double.NegativeInfinity;
    private double _heroFrameInterval = 1 / 60.0;
    private FrameDemand _heroLastDemand = FrameDemand.Full;
    private static bool _heroArtWarned;
    private double _heroTitleChangedAt = double.NegativeInfinity;
    private readonly HeroArtMix _heroArt = new();
    private BadgeLayer? _badgeLayer;
    private BadgeStage? _badgeStage;
    private HeroTitles? _badgeTitles;
    private double _heroArtMix = double.NaN;

    public OrbView(Func<double> clock)
    {
        _clock = clock;
        Scene = new OrbScene();
        _brushes = new OrbBrushes(_config.Diameter);
        _root.Children.Add(_aura);
        _root.Children.Add(_body);
        _root.Children.Add(_gauge);
        _root.Children.Add(_comet);
        _root.Children.Add(_particles);
        _root.Children.Add(_readout);
        _root.Children.Add(_overlay);
        _root.Transform = _hoverScale;
        AddVisualChild(_root);

        TextOptions.SetTextRenderingMode(_readout, TextRenderingMode.Grayscale);
        TextOptions.SetTextRenderingMode(_overlay, TextRenderingMode.Grayscale);

        _driver = new FrameDriver(Dispatcher, RenderFrame, Demand, () => Scene.NextWake, _clock);
        _accessibilityTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => FlushAccessibleName(), Dispatcher);
        _accessibilityTimer.Stop();

        IsVisibleChanged += (_, _) => UpdateActive();
        Loaded += (_, _) => UpdateActive();
        Unloaded += (_, _) => _driver.StopAll();
        Focusable = false;
        SnapsToDevicePixels = false;
    }

    public OrbScene Scene { get; }

    public double Diameter => _config.Diameter;

    /// <summary>The dashboard hero style: the vehicle replaces the readout and the view is decorative for UI Automation.</summary>
    public bool IsEmbedded => _heroConfig is not null;

    /// <summary>Spoken text such as <c>每秒 128 個 token，加速</c>; updated at most once per second.</summary>
    public string AccessibleName => _accessibleName;

    /// <summary>External pause (for example, the window is cloaked on another virtual desktop).</summary>
    public bool Paused
    {
        get => !_isActive;
        set
        {
            _pausedExternally = value;
            UpdateActive();
        }
    }

    private bool _pausedExternally;

    public void Configure(OrbConfig config)
    {
        var sizeChanged = Math.Abs(config.Diameter - _config.Diameter) > 0.01;
        _config = config;
        _tokens = OrbTokens.For(config.Appearance);
        Scene.Effects = config.Effects;
        Scene.Redline = config.Redline;
        Scene.PulsesEnabled = config.PulsesEnabled;
        Scene.PeaksEnabled = config.PeaksEnabled;
        if (sizeChanged || _brushes.Diameter != config.Diameter)
        {
            _brushes = new OrbBrushes(config.Diameter);
            InvalidateMeasure();
        }

        _text.Clear();
        _bodyDirty = true;
        _hasDrawnStatic = false;
        ConfigureHero(config);
        InvalidateLayers();
        _driver.Kick();
    }

    private void ConfigureHero(OrbConfig config)
    {
        var art = config.Hero is null ? null : VehicleArt.Shared;
        if (config.Hero is { } hero && art is not null)
        {
            _heroConfig = hero;
            if (_heroLayer is null)
            {
                _heroLayer = new HeroLayer(art);
                _root.Children.Insert(_root.Children.IndexOf(_body) + 1, _heroLayer.Root);
            }

            _heroStage ??= new HeroStage(art, hero.ParkedKey, (uint)Environment.TickCount | 1);
            _heroLayer.Configure(_brushes.Center, config.Diameter, hero.Glow);
            ConfigureBadge(hero, config.Diameter);
            _heroSettledDrawn = false;
            return;
        }

        if (config.Hero is not null && !_heroArtWarned)
        {
            _heroArtWarned = true;
            AppLog.Warn($"The hero vehicle art could not be loaded: {VehicleArt.LoadError}");
        }

        _heroConfig = null;
        if (_heroLayer is not null)
        {
            _root.Children.Remove(_heroLayer.Root);
            _heroLayer = null;
            _heroStage = null;
        }

        if (_badgeLayer is not null)
        {
            _root.Children.Remove(_badgeLayer.Root);
            _badgeLayer = null;
            _badgeStage = null;
        }
    }

    /// <summary>
    /// For every title theme but Vehicles the title badge replaces the vehicle (DESIGN 4c); a change crossfades between
    /// them in 0.3 s (Reduce Motion: cut). The art that becomes visible starts from the current state.
    /// </summary>
    private void ConfigureBadge(HeroConfig hero, double diameter)
    {
        var showsBadge = hero.Titles is not null;
        if (hero.Titles is { } titles)
        {
            _badgeTitles = titles;
        }

        if (showsBadge && _badgeLayer is null && _heroLayer is { } vehicle)
        {
            _badgeLayer = new BadgeLayer();
            _root.Children.Insert(_root.Children.IndexOf(vehicle.Root) + 1, _badgeLayer.Root);
            _badgeStage = new BadgeStage((uint)Environment.TickCount | 1);
            _heroArtMix = double.NaN;
        }

        _badgeLayer?.Configure(_brushes.Center, diameter, hero.Glow);
        var now = _clock();
        if (!_heroArt.Update(showsBadge, now, hero.Options.ReduceMotion))
        {
            return;
        }

        if (showsBadge)
        {
            _badgeStage?.Reset();
        }
        else
        {
            // The vehicle is not stepped while hidden.
            _heroResyncUntil = Math.Max(_heroResyncUntil, now + 0.05);
        }
    }

    private void InvalidateLayers()
    {
        _auraKey = long.MinValue;
        _gaugeKey = long.MinValue;
        _readoutKey = long.MinValue;
        _cometEmpty = false;
        _particlesEmpty = false;
        _overlayEmpty = false;
    }

    /// <param name="tier">Effect tier: drives effects and the gauge flash.</param>
    /// <param name="title">Speed title: the label under the value.</param>
    public void SetData(double value, SpeedTier tier, SpeedTitle title, IEnumerable<SourceKind> generating, string accessibleName)
    {
        _pendingAccessibleName = accessibleName;
        if (_pendingAccessibleName != _accessibleName && !_accessibilityTimer.IsEnabled)
        {
            FlushAccessibleName();
            _accessibilityTimer.Start();
        }

        // Idle snapshots arrive twice a second; when nothing visible changed, do not wake the renderer.
        var agents = generating as IReadOnlyCollection<SourceKind> ?? generating.ToArray();
        var target = double.IsFinite(value) && value > 0 ? value : 0;
        _heroValue = target;
        _heroTier = tier;
        if (title != _heroTitle)
        {
            _heroTitleChangedAt = _clock();
        }

        _heroTitle = title;
        var text = SpeedFormat.Speed(target);
        if (tier == _lastTier && title == _lastTitle && text == _lastText && Math.Abs(target - _lastValue) <= 1e-6 * Math.Max(1, target)
            && agents.SequenceEqual(_lastAgents))
        {
            return;
        }

        _lastTier = tier;
        _lastTitle = title;
        _lastText = text;
        _lastValue = target;
        _lastAgents = agents.ToArray();
        Scene.Update(_clock());
        Scene.SetTarget(value, tier, title);
        Scene.SetAgents(_lastAgents);
        _driver.Kick();
    }

    public void SetInfoLine(string text)
    {
        if (_infoLine == text)
        {
            return;
        }

        _infoLine = text;
        if (Scene.HoverBlend > 0.001 || _hovering)
        {
            _driver.Kick();
        }
    }

    public void SetHover(bool hovering)
    {
        if (_hovering == hovering)
        {
            return;
        }

        _hovering = hovering;
        Scene.Update(_clock());
        Scene.SetHover(hovering);
        _driver.Kick();
    }

    public void Pulse(double speed, long outputTokens)
    {
        Scene.Update(_clock());
        Scene.TriggerPulse(speed, outputTokens);
        _driver.Kick();
    }

    public void Peak()
    {
        Scene.Update(_clock());
        Scene.TriggerPeak();
        _driver.Kick();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) =>
        index == 0 ? _root : throw new ArgumentOutOfRangeException(nameof(index));

    protected override Size MeasureOverride(Size availableSize) => new(Diameter * 1.5, Diameter * 1.5);

    protected override Size ArrangeOverride(Size finalSize) => new(Diameter * 1.5, Diameter * 1.5);

    protected override AutomationPeer OnCreateAutomationPeer() => new OrbAutomationPeer(this);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _text.PixelsPerDip = newDpi.PixelsPerDip;
        _hasDrawnStatic = false;
        _heroSettledDrawn = false;
        _driver.Kick();
    }

    private void UpdateActive()
    {
        var active = IsVisible && !_pausedExternally;
        if (active && !_isActive)
        {
            // Reopened: the vehicle shows the current state at once instead of replaying what happened while hidden.
            _heroResyncUntil = _clock() + 0.35;
            _badgeStage?.Reset();
            _heroSettledDrawn = false;
            if (_heroLayer is not null)
            {
                _heroFrameInterval = 1 / HeroStage.MovingFrameRate(RefreshRate());
            }
        }

        _isActive = active;
        _driver.Enabled = _isActive;
    }

    /// <summary>Refresh rate of the display showing this view (60 when unknown).</summary>
    private int RefreshRate()
    {
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source
            && Interop.Monitors.FromWindow(source.Handle) is { } monitor
            && Interop.Monitors.RefreshRate(monitor) is { } rate)
        {
            return rate;
        }

        return 60;
    }

    private void FlushAccessibleName()
    {
        if (_pendingAccessibleName == _accessibleName)
        {
            _accessibilityTimer.Stop();
            return;
        }

        _accessibleName = _pendingAccessibleName;
        if (!IsEmbedded && UIElementAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, string.Empty, _accessibleName);
        }
    }

    private void RenderFrame()
    {
        _text.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        Scene.Update(_clock());

        var hover = Scene.Hover;
        var scale = Math.Round((1 + 0.03 * hover) * 2000) / 2000;
        if (scale != _lastScale)
        {
            var c = _brushes.Center;
            _hoverScale.CenterX = c.X;
            _hoverScale.CenterY = c.Y;
            _hoverScale.ScaleX = scale;
            _hoverScale.ScaleY = scale;
            _lastScale = scale;
        }

        if (_bodyDirty || !_hasDrawnStatic)
        {
            DrawBody();
            _bodyDirty = false;
            _hasDrawnStatic = true;
            InvalidateLayers();
            _heroSettledDrawn = false;
        }

        RenderHero();

        // Each layer is re-recorded only when its inputs changed, so an idle frame touches (and dirties) nothing.
        var auraKey = AuraKey();
        if (auraKey != _auraKey)
        {
            _auraKey = auraKey;
            DrawAura();
        }

        var gaugeKey = GaugeKey();
        if (gaugeKey == long.MaxValue || gaugeKey != _gaugeKey)
        {
            _gaugeKey = gaugeKey;
            DrawGauge();
        }

        var cometEmpty = Scene.CometOpacity <= 0.005 || _config.HighContrast;
        if (!(cometEmpty && _cometEmpty))
        {
            _cometEmpty = cometEmpty;
            DrawComet();
        }

        var particlesEmpty = Scene.Particles.Count == 0;
        if (!(particlesEmpty && _particlesEmpty))
        {
            _particlesEmpty = particlesEmpty;
            DrawParticles();
        }

        var readoutKey = ReadoutKey();
        if (readoutKey == long.MaxValue || readoutKey != _readoutKey)
        {
            _readoutKey = readoutKey;
            DrawReadout();
        }

        var overlayEmpty = _config.HighContrast || (Scene.PulseCount == 0 && Scene.PeakRays is null);
        if (!(overlayEmpty && _overlayEmpty))
        {
            _overlayEmpty = overlayEmpty;
            DrawOverlay();
        }
    }

    private FrameDemand Demand()
    {
        var demand = !_valueWidth.IsSettled(_valueWidthTarget, 0.05) ? FrameDemand.Full : Scene.Demand;
        var hero = HeroDemand();
        return hero > demand ? hero : demand;
    }

    /// <summary>Frames the hero's center needs: those of the art shown, every frame while they crossfade.</summary>
    private FrameDemand HeroDemand()
    {
        if (_heroStage is not { } stage)
        {
            return FrameDemand.None;
        }

        if (_heroArt.IsFading(Scene.Time))
        {
            return FrameDemand.Full;
        }

        return _heroArt.ShowsBadge == true && _badgeStage is { } badge ? BadgeFrameRates.ToFrameDemand(badge.Demand) : stage.Demand;
    }

    /// <summary>
    /// Advances and draws the hero's center art: the vehicle, the title badge, or both while they crossfade. With Off and
    /// Reduce Motion a settled art is drawn once and then left alone, even while the ring keeps animating.
    /// </summary>
    private void RenderHero()
    {
        if (_heroLayer is not { } layer || _heroStage is not { } stage || _heroConfig is not { } hero)
        {
            return;
        }

        var started = HeroStats.Begin(out var bytes);
        var now = Scene.Time;
        var mix = _heroArt.BadgeMix(now);
        var vehicleShown = mix < 0.999;
        var badge = mix > 0.001 ? _badgeStage : null;
        var titles = _badgeTitles;
        var sweep = Scene.IdleSweepStart;
        var origin = double.IsFinite(sweep) ? sweep : double.NaN;
        if (vehicleShown)
        {
            stage.IdleSweepOrigin = origin;
            stage.Update(now, _heroValue, _config.Redline, _heroTier, _heroTitle, hero.Options, snap: now <= _heroResyncUntil);
        }

        if (badge is not null && titles is not null)
        {
            badge.Step(new BadgeInput(now, (int)_heroTitle, _heroTitleChangedAt, Scene.Heat, Scene.Overdrive, _heroTier, _config.Redline,
                hero.Options, origin), titles.Labels);
        }
        else
        {
            badge = null;
        }

        // Moving: the largest divisor of the refresh rate that is at least 60 fps; parked: about 15 fps (30 while the
        // badge's light band crosses); static: drawn once. The ring may ask for more frames; the art keeps its own pace.
        var (demand, interval) = HeroPace(stage, vehicleShown, badge);
        if (_heroSettledDrawn && demand == _heroLastDemand && now - _heroLastRender < interval)
        {
            return;
        }

        if (vehicleShown)
        {
            layer.Render(stage, _config.HighContrast ? SystemColors.HighlightColor.ToRgba() : null);
        }
        else
        {
            layer.Clear();
        }

        if (badge is not null && titles is not null)
        {
            _badgeLayer?.Render(badge, titles.LanguageCode,
                _config.HighContrast ? new BadgeContrast(SystemColors.HighlightColor.ToRgba(), SystemColors.GrayTextColor.ToRgba()) : null);
        }
        else
        {
            _badgeLayer?.Clear();
        }

        if (mix != _heroArtMix)
        {
            _heroArtMix = mix;
            layer.Root.Opacity = 1 - mix;
            if (_badgeLayer is not null)
            {
                _badgeLayer.Root.Opacity = mix;
            }
        }

        _heroLastRender = now;
        _heroLastDemand = demand;
        _heroSettledDrawn = true;
        _heroStats.End(started, bytes, badge is null ? stage.Parked ? "parked" : stage.CurrentKey
            : vehicleShown ? "crossfade" : badge.Level == 0 ? "badge idle" : "badge");
    }

    /// <summary>The frame demand of the art shown and the shortest interval between its frames.</summary>
    private (FrameDemand Demand, double Interval) HeroPace(HeroStage stage, bool vehicleShown, BadgeStage? badge)
    {
        var moving = 0.75 * _heroFrameInterval;
        if (badge is not null)
        {
            return vehicleShown ? (FrameDemand.Full, moving) : badge.Demand switch
            {
                BadgeFrames.None => (FrameDemand.None, double.PositiveInfinity),
                BadgeFrames.Parked => (FrameDemand.Medium, 1 / BadgeFrameRates.Parked - 0.008),
                BadgeFrames.Sweep => (FrameDemand.Full, 1 / BadgeFrameRates.Sweep - 0.004),
                _ => (FrameDemand.Full, moving),
            };
        }

        var demand = stage.Demand;
        return (demand, demand switch
        {
            FrameDemand.None => double.PositiveInfinity,
            FrameDemand.Full => moving,
            _ => 1 / HeroStage.ParkedFrameRate - 0.008,
        });
    }

    private long AuraKey()
    {
        var intensity = Scene.AuraIntensity * _tokens.AuraOpacityScale;
        if (intensity <= 0.004 || _config.HighContrast)
        {
            return -1;
        }

        return HeatPalette.Bucket(Scene.Heat, 48) * 100_000L + (long)Math.Round(intensity * 1000);
    }

    /// <summary>Change key of the gauge layer; <see cref="long.MaxValue"/> means it animates every frame.</summary>
    private long GaugeKey()
    {
        var animated = (Scene.Effects.Shimmer && Scene.Tier == SpeedTier.Redline)
            || (Scene.Effects.HeatDistortion && Scene.Tier == SpeedTier.Redline)
            || Scene.Flash > 0
            || Scene.EdgeGlow > 0.01;
        if (animated)
        {
            return long.MaxValue;
        }

        var sweep = Scene.IdleSweep is { } p ? (long)Math.Round(p * 1000) + 1 : 0;
        return (long)Math.Round(Scene.GaugeHeat * 100_000) * 10_000 + sweep;
    }

    /// <summary>Change key of the readout layer; <see cref="long.MaxValue"/> means it animates every frame.</summary>
    private long ReadoutKey()
    {
        if (IsEmbedded)
        {
            return EmbeddedReadoutKey();
        }

        var now = Scene.Time;
        var animated = Scene.Readout.IsAnimating(now)
            || Scene.Transition.Progress < 1
            || Scene.TitleTransition.Progress < 1
            || Scene.PeakPillAge is not null
            || Scene.JitterX != 0 || Scene.JitterY != 0
            || (Scene.AgentDots.Count > 0 && Scene.Effects.AuraBreathing)
            || (Scene.HoverBlend > 0.001 && Scene.HoverBlend < 0.999)
            || !_valueWidth.IsSettled(_valueWidthTarget, 0.05);
        if (animated)
        {
            return long.MaxValue;
        }

        var hash = new HashCode();
        hash.Add(Scene.Readout.Current);
        hash.Add(Scene.Tier);
        hash.Add(Scene.Title);
        hash.Add(Math.Round(Scene.Heat * 63));
        hash.Add(Math.Round(Scene.Overdrive * 63));
        hash.Add(Math.Round(Scene.SkewDegrees * 20));
        hash.Add(Scene.HoverBlend > 0.5);
        hash.Add(_infoLine);
        hash.Add(Scene.AgentDots.Count);
        hash.Add(Scene.AgentOverflow);
        foreach (var agent in Scene.AgentDots)
        {
            hash.Add(agent);
        }

        return hash.ToHashCode() & 0x7FFF_FFFFL;
    }

    /// <summary>Embedded style: only the agent dots and the new-peak pill remain of the readout.</summary>
    private long EmbeddedReadoutKey()
    {
        if (Scene.PeakPillAge is not null || (Scene.AgentDots.Count > 0 && Scene.Effects.AuraBreathing))
        {
            return long.MaxValue;
        }

        var hash = new HashCode();
        hash.Add(Scene.AgentDots.Count);
        hash.Add(Scene.AgentOverflow);
        foreach (var agent in Scene.AgentDots)
        {
            hash.Add(agent);
        }

        return hash.ToHashCode() & 0x7FFF_FFFFL;
    }

    private void DrawAura()
    {
        using var dc = _aura.RenderOpen();
        var intensity = Scene.AuraIntensity * _tokens.AuraOpacityScale;
        if (intensity <= 0.004 || _config.HighContrast)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        dc.PushOpacity(Math.Clamp(intensity, 0, 1));
        dc.DrawEllipse(_brushes.Aura(Scene.Heat), null, c, s * 0.75, s * 0.75);
        dc.Pop();
    }

    private void DrawBody()
    {
        using var dc = _body.RenderOpen();
        var s = Diameter;
        var c = _brushes.Center;
        var r = s / 2;

        if (_config.HighContrast)
        {
            dc.DrawEllipse(SystemColors.WindowBrush, new Pen(SystemColors.WindowTextBrush, 1.5), c, r - 0.75, r - 0.75);
            return;
        }

        var t = _tokens;
        var shadowRadius = r + t.ShadowBlur * s;
        var shadow = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(t.ShadowColor.ToColor(), 0),
                new GradientStop(t.ShadowColor.ToColor(), (r - t.ShadowBlur * s * 0.5) / shadowRadius),
                new GradientStop(t.ShadowColor.ToColor(0.45), r / shadowRadius),
                new GradientStop(t.ShadowColor.ToColor(0.12), (r + t.ShadowBlur * s * 0.5) / shadowRadius),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(c.X, c.Y + t.ShadowOffset * s), shadowRadius, shadowRadius);

        var alpha = _config.SolidBody ? 1 : t.BodyAlpha * Math.Clamp(_config.BodyOpacity, 0.4, 1);
        var body = new LinearGradientBrush(t.BodyTop.ToColor(alpha), t.BodyBottom.ToColor(alpha), 90);
        body.Freeze();
        dc.DrawEllipse(body, null, c, r, r);

        var innerShadow = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.56),
            GradientOrigin = new Point(0.5, 0.62),
            GradientStops =
            {
                new GradientStop(Colors.Transparent, 0),
                new GradientStop(Colors.Transparent, 0.78),
                new GradientStop(Color.FromArgb(t.Appearance == OrbAppearance.Dark ? (byte)70 : (byte)22, 0, 0, 0), 1),
            },
        };
        innerShadow.Freeze();
        dc.DrawEllipse(innerShadow, null, c, r, r);

        var lens = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(t.Appearance == OrbAppearance.Dark ? (byte)16 : (byte)90, 255, 255, 255), 0),
                new GradientStop(Colors.Transparent, 1),
            },
        };
        lens.Freeze();
        dc.DrawEllipse(lens, null, new Point(c.X - 0.17 * s, c.Y - 0.19 * s), 0.3 * s, 0.24 * s);

        var rim = new LinearGradientBrush(t.RimTop.ToColor(), t.RimTop.ToColor(0), 90);
        rim.Freeze();
        dc.DrawEllipse(null, MediaColors.FrozenPen(rim, 1), c, r - 0.5, r - 0.5);

        if (t.OuterStroke is { } outer)
        {
            dc.DrawEllipse(null, MediaColors.FrozenPen(outer.ToBrush(), 1), c, r + 0.5, r + 0.5);
        }
    }

    private void DrawGauge()
    {
        using var dc = _gauge.RenderOpen();
        var s = Diameter;
        var c = _brushes.Center;
        var heat = Scene.GaugeHeat;
        var flash = Scene.Flash;
        var shimmer = Scene.Shimmer;
        var sweep = Scene.IdleSweep;
        var hc = _config.HighContrast;
        var unlit = hc ? SystemColors.GrayTextColor.ToRgba().WithAlpha(0.6) : _tokens.UnlitSegment;
        var leading = GaugeLayout.LeadingIndex(heat);

        for (var i = 0; i < GaugeLayout.SegmentCount; i++)
        {
            var geometry = _brushes.Segments[i];
            var unlitAlpha = 1.0;
            if (sweep is { } p)
            {
                var position = i / (double)(GaugeLayout.SegmentCount - 1);
                var distance = Scene.Effects.SlideTransitions ? Math.Abs(position - (p * 1.3 - 0.15)) : 0;
                var strength = Scene.Effects.SlideTransitions ? Math.Exp(-distance * distance * 90) : Math.Sin(Math.PI * p) * 0.6;
                unlitAlpha += 1.6 * strength;
            }

            dc.DrawGeometry(_brushes.Solid.Get(unlit, unlitAlpha), null, geometry);

            var fill = GaugeLayout.SegmentFill(heat, i);
            if (fill <= 0.002)
            {
                continue;
            }

            Rgba color;
            if (hc)
            {
                color = SystemColors.HighlightColor.ToRgba();
            }
            else if (i == leading)
            {
                color = _tokens.LeadingSegment(heat);
            }
            else
            {
                color = HeatPalette.Sample(GaugeLayout.SegmentHeat(i));
                if (flash > 0)
                {
                    color = OkLab.Mix(color, HeatPalette.WhiteHot, 0.25 * flash);
                }
            }

            var brightness = fill * shimmer[i];
            dc.DrawGeometry(_brushes.Solid.Get(color, brightness), null, geometry);
        }

        if (!hc && leading >= 0)
        {
            var arc = GaugeLayout.Segment(leading);
            var center = OrbBrushes.Polar(c, arc.MidRadius * s, arc.MidDegrees);
            var bloomRadius = (GaugeLayout.SegmentLength * 0.5 + 0.03) * s * Scene.Effects.Bloom;
            var bloomColor = _tokens.Appearance == OrbAppearance.Dark
                ? OkLab.Mix(HeatPalette.Sample(Q(heat)), HeatPalette.WhiteHot, 0.35)
                : HeatPalette.Sample(Q(heat));
            dc.PushOpacity(Math.Clamp(0.55 + 0.45 * GaugeLayout.SegmentFill(heat, leading), 0, 1) * (0.6 + 0.4 * heat));
            dc.DrawEllipse(_brushes.Bloom(bloomColor), null, center, bloomRadius, bloomRadius);
            dc.Pop();
        }

        var edgeGlow = Scene.EdgeGlow;
        if (!hc && edgeGlow > 0.01 && heat > 0.02)
        {
            var end = GaugeLayout.AngleForFraction(heat);
            var glowColor = HeatPalette.Highlight(Q(heat), Q(Scene.Overdrive));
            var arc = OrbBrushes.Arc(c, (GaugeLayout.OuterRadius + 0.012) * s, GaugeLayout.StartDegrees, end);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(glowColor, edgeGlow * 0.35), 0.05 * s, PenLineCap.Round), arc);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(glowColor, edgeGlow * 0.6), 0.016 * s, PenLineCap.Round), arc);
        }

        foreach (var (_, fraction) in GaugeLayout.Ticks(_config.Redline))
        {
            var lit = heat >= fraction - 0.0001;
            var point = OrbBrushes.Polar(c, GaugeLayout.TickRadius * s, GaugeLayout.AngleForFraction(fraction));
            var color = hc
                ? SystemColors.WindowTextColor.ToRgba()
                : lit ? HeatPalette.Sample(fraction) : _tokens.SecondaryText;
            var radius = Math.Max(0.9, 0.0085 * s);
            dc.DrawEllipse(_brushes.Solid.Get(color, lit ? 0.95 : 0.55), null, point, radius, radius);
        }

        if (Scene.Effects.HeatDistortion && Scene.Tier == SpeedTier.Redline && !hc)
        {
            var t = Scene.Time;
            var color = HeatPalette.Highlight(Q(heat), Q(Scene.Overdrive));
            for (var k = 0; k < 3; k++)
            {
                var radius = (0.515 + 0.03 * k + 0.008 * Math.Sin(t * 5.3 + k * 2.1)) * s;
                dc.DrawEllipse(null, MediaColors.FrozenPen(_brushes.Solid.Get(color, 0.05 + 0.03 * Math.Sin(t * 3.7 + k)), 0.012 * s), c, radius, radius * (1 + 0.01 * Math.Sin(t * 4.1 + k)));
            }
        }
    }

    private void DrawComet()
    {
        using var dc = _comet.RenderOpen();
        var opacity = Scene.CometOpacity;
        if (opacity <= 0.005 || _config.HighContrast)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        var head = Scene.CometDegrees;
        var heat = Q(Scene.Heat);
        var color = HeatPalette.Sample(heat);
        var hot = _tokens.Appearance == OrbAppearance.Dark ? HeatPalette.WhiteHot : OkLab.Darken(color, 0.1);
        const int pieces = 16;
        const double length = 40;
        var radius = GaugeLayout.CometRadius * s;

        for (var i = 0; i < pieces; i++)
        {
            var t0 = i / (double)pieces;
            var t1 = (i + 1) / (double)pieces;
            var a0 = head - length + length * t0;
            var a1 = head - length + length * t1 + 0.4;
            var gap = GapFade((a0 + a1) / 2);
            if (gap <= 0.01)
            {
                continue;
            }

            var mix = t1 * t1;
            var pieceColor = OkLab.Mix(color, hot, Math.Max(0, (t1 - 0.6) / 0.4));
            var alpha = opacity * Math.Pow(t1, 1.6) * gap;
            var thickness = (0.006 + 0.012 * mix) * s;
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(pieceColor, alpha), thickness),
                OrbBrushes.Arc(c, radius, a0, a1));
        }

        var headGap = GapFade(head);
        if (headGap > 0.01)
        {
            var point = OrbBrushes.Polar(c, radius, head);
            var glow = 0.035 * s * Scene.Effects.Bloom;
            dc.PushOpacity(Math.Clamp(opacity * headGap, 0, 1));
            dc.DrawEllipse(_brushes.Bloom(hot), null, point, glow, glow);
            dc.Pop();
        }
    }

    /// <summary>Quantizes a 0..1 value so per-frame colors reuse cached brushes.</summary>
    private static double Q(double value) => Math.Round(Math.Clamp(value, 0, 1) * 63) / 63;

    private static double GapFade(double degrees)
    {
        var a = ((degrees % 360) + 360) % 360;
        var distance = Math.Abs(a - 90);
        return Easing.SmoothStep(38, 62, distance);
    }

    private void DrawParticles()
    {
        using var dc = _particles.RenderOpen();
        var particles = Scene.Particles.Particles;
        if (particles.Length == 0)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        var dark = _tokens.Appearance == OrbAppearance.Dark;
        var sizeScale = _tokens.ParticleSizeScale * Math.Clamp(s / 148, 0.75, 1.4);
        foreach (ref readonly var p in particles)
        {
            var progress = p.Progress;
            var point = new Point(c.X + p.X * s, c.Y + p.Y * s);
            var color = ParticleColor(p, progress, dark);
            var alpha = p.Kind == ParticleKind.Spark
                ? Math.Pow(1 - progress, 1.3)
                : Math.Sin(Math.PI * Math.Min(1, progress * 1.15)) * 0.9;
            var radius = p.Size * 0.5 * sizeScale * (p.Kind == ParticleKind.Ember ? 1 - 0.35 * progress : 1);
            if (dark)
            {
                dc.DrawEllipse(_brushes.Solid.Get(color, alpha * 0.22), null, point, radius * 2.4, radius * 2.4);
            }

            dc.DrawEllipse(_brushes.Solid.Get(color, alpha), null, point, radius, radius);
        }
    }

    private Rgba ParticleColor(in Particle p, double progress, bool dark)
    {
        var heat = Math.Round(p.Heat * 23) / 23;
        var step = Math.Round(progress * 7) / 7;
        var baseColor = HeatPalette.Sample(heat);
        var hot = dark ? HeatPalette.WhiteHot : HeatPalette.LightSparkHot;
        return p.Kind == ParticleKind.Spark
            ? OkLab.Mix(hot, baseColor, Math.Min(1, step * 1.4))
            : OkLab.Mix(HeatPalette.Highlight(heat, 0.3), baseColor, step);
    }

    private void DrawReadout()
    {
        _text.TrimStyles();
        using var dc = _readout.RenderOpen();
        var s = Diameter;
        var c = _brushes.Center;
        var now = Scene.Time;
        var hc = _config.HighContrast;
        var idle = Scene.Tier == SpeedTier.Idle && Scene.Value <= 0;

        var valueColor = hc ? SystemColors.WindowTextColor.ToRgba() : idle ? _tokens.IdleValueText : _tokens.ValueText;
        var secondary = hc ? SystemColors.WindowTextColor.ToRgba() : _tokens.SecondaryText;
        var valueStyle = _text.Style(ValueFace, 0.24 * s, _brushes.Solid.Get(valueColor));
        var capHeight = (ValueFace.CapsHeight > 0 ? ValueFace.CapsHeight : 0.7) * 0.24 * s;
        var baseline = c.Y - 0.035 * s + capHeight / 2;
        if (IsEmbedded)
        {
            DrawAgentDots(dc, c, s, hc);
            DrawPeakPill(dc, c, s, baseline - capHeight - 0.075 * s);
            return;
        }

        var digitCell = 0.0;
        for (var d = '0'; d <= '9'; d++)
        {
            digitCell = Math.Max(digitCell, _text.Get(d.ToString(), valueStyle).WidthIncludingTrailingWhitespace);
        }

        var readout = Scene.Readout;
        var targetWidth = 0.0;
        foreach (var ch in readout.Current)
        {
            targetWidth += char.IsDigit(ch) ? digitCell : _text.Get(ch.ToString(), valueStyle).WidthIncludingTrailingWhitespace;
        }

        _valueWidthTarget = targetWidth;
        var dt = Math.Clamp(now - _lastReadoutTime, 0, 0.1);
        _lastReadoutTime = now;
        if (Scene.Effects.SlideTransitions && _valueWidth.Value > 0)
        {
            _valueWidth.Step(targetWidth, dt);
        }
        else
        {
            _valueWidth.Snap(targetWidth);
        }

        var right = c.X + _valueWidth.Value / 2;

        dc.PushTransform(new TranslateTransform(Scene.JitterX, Scene.JitterY));
        dc.PushTransform(new SkewTransform(Scene.SkewDegrees, 0, c.X, baseline - capHeight / 2));

        var glow = !hc && Scene.Effects.ValueGlow && Scene.Tier >= SpeedTier.Boost;
        var glowColor = HeatPalette.Highlight(Q(Scene.Heat), Q(Scene.Overdrive));
        // The roll is clipped to the digits' own line box so nothing ever crosses the unit or the pill.
        var clip = new RectangleGeometry(new Rect(c.X - 0.46 * s, baseline - capHeight - 0.02 * s, 0.92 * s, capHeight + 0.034 * s));
        clip.Freeze();
        var travel = capHeight * 1.12;
        var glowTint = glow ? glowColor : (Rgba?)null;

        var x = right;
        for (var slot = 0; slot < readout.SlotCount; slot++)
        {
            var info = readout.Slot(slot, now);
            var currentChar = info.Current;
            var cell = currentChar is { } cc
                ? char.IsDigit(cc) ? digitCell : _text.Get(cc.ToString(), valueStyle).WidthIncludingTrailingWhitespace
                : info.Previous is { } pc2 && char.IsDigit(pc2) ? digitCell : 0;
            x -= cell;

            if (!info.IsChanging)
            {
                if (currentChar is { } resting)
                {
                    DrawChar(dc, resting, valueStyle, x, cell, baseline, 1, digitCell, glowTint, s);
                }

                continue;
            }

            var eased = RollingText.Ease(info.Progress);
            dc.PushClip(clip);
            if (info.Previous is { } previous)
            {
                DrawChar(dc, previous, valueStyle, x, cell, baseline + travel * info.PreviousPosition(eased),
                    info.PreviousOpacity(eased), digitCell, glowTint, s);
            }

            if (currentChar is { } current)
            {
                DrawChar(dc, current, valueStyle, x, cell, baseline + travel * info.CurrentPosition(eased),
                    info.CurrentOpacity(eased), digitCell, glowTint, s);
            }

            dc.Pop();
        }

        dc.Pop();
        dc.Pop();

        var unitStyle = _text.Style(UnitFace, 0.072 * s, _brushes.Solid.Get(secondary));
        DrawTracked(dc, _config.UnitText, unitStyle, c.X, baseline + 0.105 * s, 0.5 * s / 148, 1);

        DrawTitleLabel(dc, c, baseline + 0.19 * s, hc);
        DrawAgentDots(dc, c, s, hc);
        DrawPeakPill(dc, c, s, baseline - capHeight - 0.075 * s);
    }

    private void DrawChar(DrawingContext dc, char ch, int style, double x, double cell, double baseline, double alpha,
        double digitCell, Rgba? glow, double s)
    {
        if (alpha <= 0.01)
        {
            return;
        }

        var text = ch.ToString();
        var formatted = _text.Get(text, style);
        var offsetX = char.IsDigit(ch) ? (digitCell - formatted.WidthIncludingTrailingWhitespace) / 2 : (cell - formatted.WidthIncludingTrailingWhitespace) / 2;
        var origin = new Point(x + offsetX, baseline - formatted.Baseline);

        if (glow is { } g)
        {
            var outline = _text.Outline(text, style);
            dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
            var radius = 0.035 * s * Scene.Effects.Bloom;
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(g, 0.10 * alpha), radius * 1.6, PenLineCap.Round), outline);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(g, 0.16 * alpha), radius * 0.9, PenLineCap.Round), outline);
            dc.DrawGeometry(null, MediaColors.FrozenPen(_brushes.Solid.Get(g, 0.22 * alpha), radius * 0.35, PenLineCap.Round), outline);
            dc.Pop();
        }

        if (alpha < 0.999)
        {
            dc.PushOpacity(alpha);
        }

        dc.DrawText(formatted, origin);
        if (alpha < 0.999)
        {
            dc.Pop();
        }
    }

    /// <summary>Draws text centered on <paramref name="centerX"/> with extra letter spacing (tracking) in DIP.</summary>
    private void DrawTracked(DrawingContext dc, string text, int style, double centerX, double baseline, double tracking, double alpha)
    {
        if (string.IsNullOrEmpty(text) || alpha <= 0.01)
        {
            return;
        }

        var useTracking = tracking > 0.01 && IsLatin(text);
        double width;
        if (!useTracking)
        {
            var whole = _text.Get(text, style);
            width = whole.WidthIncludingTrailingWhitespace;
            if (alpha < 0.999)
            {
                dc.PushOpacity(alpha);
            }

            dc.DrawText(whole, new Point(centerX - width / 2, baseline - whole.Baseline));
            if (alpha < 0.999)
            {
                dc.Pop();
            }

            return;
        }

        width = 0;
        foreach (var ch in text)
        {
            width += _text.Get(ch.ToString(), style).WidthIncludingTrailingWhitespace + tracking;
        }

        width -= tracking;
        var x = centerX - width / 2;
        if (alpha < 0.999)
        {
            dc.PushOpacity(alpha);
        }

        foreach (var ch in text)
        {
            var glyph = _text.Get(ch.ToString(), style);
            dc.DrawText(glyph, new Point(x, baseline - glyph.Baseline));
            x += glyph.WidthIncludingTrailingWhitespace + tracking;
        }

        if (alpha < 0.999)
        {
            dc.Pop();
        }
    }

    private void DrawTitleLabel(DrawingContext dc, Point c, double baseline, bool hc)
    {
        var s = Diameter;
        var size = OrbLabelFit.Size * s;
        var tracking = 1.6 * s / 148;
        var available = OrbLabelFit.AvailableWidth((baseline - c.Y) / s) * s;
        var heat = Math.Round(Scene.Heat * 31) / 31;
        var hoverBlend = Scene.HoverBlend;
        var labelAlpha = 1 - hoverBlend;

        var transition = Scene.TitleTransition;
        if (transition.Progress < 1 && transition.From != transition.To)
        {
            var t = Easing.OutCubic(transition.Progress);
            var rise = Scene.Effects.SlideTransitions ? size * 1.1 : 0;
            DrawFittedTitle(dc, transition.From, c.X, baseline - rise * t, size, tracking, available, heat, hc, (1 - t) * labelAlpha);
            DrawFittedTitle(dc, transition.To, c.X, baseline + rise * (1 - t), size, tracking, available, heat, hc, t * labelAlpha);
        }
        else
        {
            DrawFittedTitle(dc, Scene.Title, c.X, baseline, size, tracking, available, heat, hc, labelAlpha);
        }

        if (hoverBlend > 0.01 && !string.IsNullOrEmpty(_infoLine))
        {
            var secondary = hc ? SystemColors.WindowTextColor.ToRgba() : _tokens.SecondaryText;
            var infoStyle = _text.Style(LabelFace, size * 0.95, _brushes.Solid.Get(secondary));
            var info = _text.Get(_infoLine, infoStyle);
            var maxWidth = s * 0.7;
            if (info.WidthIncludingTrailingWhitespace > maxWidth)
            {
                info.MaxTextWidth = maxWidth;
                info.Trimming = TextTrimming.CharacterEllipsis;
                info.MaxLineCount = 1;
            }

            dc.PushOpacity(hoverBlend);
            var width = Math.Min(info.WidthIncludingTrailingWhitespace, maxWidth);
            dc.DrawText(info, new Point(c.X - width / 2, baseline - info.Baseline));
            dc.Pop();
        }
    }

    /// <summary>A title in the heat color, shrunk (never below 80 %) when it would reach the gauge ring.</summary>
    private void DrawFittedTitle(DrawingContext dc, SpeedTitle title, double centerX, double baseline, double size, double tracking,
        double available, double heat, bool hc, double alpha)
    {
        var text = _config.TitleLabel(title);
        if (string.IsNullOrEmpty(text) || alpha <= 0.01)
        {
            return;
        }

        var color = hc ? SystemColors.WindowTextColor.ToRgba()
            : title == SpeedTitle.Idle ? _tokens.SecondaryText : _tokens.TierLabel(Math.Max(heat, 0.08));
        var brush = _brushes.Solid.Get(color);
        var natural = TrackedWidth(text, _text.Style(LabelFace, size, brush), tracking);
        var scale = OrbLabelFit.Scale(natural, available);
        var style = scale < 0.999 ? _text.Style(LabelFace, size * scale, brush) : _text.Style(LabelFace, size, brush);
        DrawTracked(dc, text, style, centerX, baseline, tracking * scale, alpha);
    }

    private double TrackedWidth(string text, int style, double tracking)
    {
        if (!(tracking > 0.01 && IsLatin(text)))
        {
            return _text.Get(text, style).WidthIncludingTrailingWhitespace;
        }

        var width = -tracking;
        foreach (var ch in text)
        {
            width += _text.Get(ch.ToString(), style).WidthIncludingTrailingWhitespace + tracking;
        }

        return width;
    }

    private void DrawAgentDots(DrawingContext dc, Point c, double s, bool hc)
    {
        var agents = Scene.AgentDots;
        if (agents.Count == 0)
        {
            return;
        }

        var unit = s / 148;
        var radius = Math.Max(1.5, 2 * unit);
        var spacing = 8 * unit;
        var overflow = Scene.AgentOverflow;
        var count = agents.Count + (overflow > 0 ? 1 : 0);
        var x = c.X - (count - 1) * spacing / 2;
        var y = c.Y + 0.355 * s;
        var pulse = Scene.Effects.AuraBreathing;
        for (var i = 0; i < agents.Count; i++)
        {
            var alpha = pulse ? 0.62 + 0.38 * (0.5 + 0.5 * Math.Sin(Scene.Time * 4.2 + i * 1.3)) : 0.9;
            var color = hc ? SystemColors.WindowTextColor.ToRgba() : HeatPalette.SourceColor(agents[i]);
            dc.DrawEllipse(_brushes.Solid.Get(color, alpha), null, new Point(x + i * spacing, y), radius, radius);
        }

        if (overflow > 0)
        {
            var secondary = hc ? SystemColors.WindowTextColor.ToRgba() : _tokens.SecondaryText;
            var style = _text.Style(UnitFace, Math.Max(7, 0.05 * s), _brushes.Solid.Get(secondary));
            var text = _text.Get("+" + overflow.ToString(System.Globalization.CultureInfo.InvariantCulture), style);
            dc.DrawText(text, new Point(x + agents.Count * spacing - radius, y - text.Height / 2));
        }
    }

    private void DrawPeakPill(DrawingContext dc, Point c, double s, double centerY)
    {
        if (Scene.PeakPillAge is not { } age || string.IsNullOrEmpty(_config.PeakText))
        {
            return;
        }

        var appear = Scene.Effects.SlideTransitions ? Easing.OutBack(Math.Min(1, age / 0.45), 2.2) : Math.Min(1, age / 0.2);
        var fade = age > OrbScene.PeakPillDuration - 0.3 ? (OrbScene.PeakPillDuration - age) / 0.3 : 1;
        var alpha = Math.Clamp(Math.Min(age / 0.12, 1) * fade, 0, 1);
        if (alpha <= 0.01)
        {
            return;
        }

        var style = _text.Style(LabelFace, 0.056 * s, _brushes.Solid.Get(Rgba.FromHex("#1A1300")));
        var text = _text.Get(_config.PeakText, style);
        var width = text.WidthIncludingTrailingWhitespace + 0.07 * s;
        var height = text.Height + 0.022 * s;
        var scale = Scene.Effects.SlideTransitions ? 0.6 + 0.4 * appear : 1;

        dc.PushOpacity(alpha);
        dc.PushTransform(new ScaleTransform(scale, scale, c.X, centerY));
        var rect = new Rect(c.X - width / 2, centerY - height / 2, width, height);
        dc.DrawRoundedRectangle(_brushes.Bloom(HeatPalette.Gold), null, Inflate(rect, 0.04 * s), height, height);
        dc.DrawRoundedRectangle(_brushes.Solid.Get(HeatPalette.Gold), null, rect, height / 2, height / 2);
        dc.DrawText(text, new Point(c.X - text.WidthIncludingTrailingWhitespace / 2, centerY - text.Height / 2));
        dc.Pop();
        dc.Pop();
    }

    private void DrawOverlay()
    {
        using var dc = _overlay.RenderOpen();
        if (_config.HighContrast)
        {
            return;
        }

        var s = Diameter;
        var c = _brushes.Center;
        var dark = _tokens.Appearance == OrbAppearance.Dark;
        foreach (var wave in Scene.Pulses)
        {
            var eased = Easing.OutCubic(wave.Progress);
            var radius = Scene.Effects.PulseExpands ? (0.5 + 0.24 * eased) * s : 0.51 * s;
            var strength = Math.Clamp(wave.Strength, 0.3, 1.25);
            var thickness = Math.Max(0.3, 2 * strength * (1 - eased));
            var alpha = 0.6 * (1 - eased) * Math.Min(1, strength);
            var color = dark ? OkLab.Mix(HeatPalette.Sample(wave.Heat), HeatPalette.WhiteHot, 0.15) : HeatPalette.Sample(wave.Heat);
            dc.DrawEllipse(null, MediaColors.FrozenPen(_brushes.Solid.Get(color, alpha), thickness), c, radius, radius);
        }

        if (Scene.PeakRays is { } t)
        {
            var reach = Scene.Effects.SlideTransitions ? Easing.OutCubic(t) : 1;
            var alpha = Math.Pow(1 - t, 1.5);
            var pen = MediaColors.FrozenPen(_brushes.Solid.Get(HeatPalette.Gold, alpha), Math.Max(1, 1.6 * s / 148), PenLineCap.Round);
            for (var i = 0; i < 24; i++)
            {
                var angle = i * 15 + 7.5;
                var inner = (0.505 + 0.02 * reach) * s;
                var outer = (0.52 + 0.2 * reach * (i % 2 == 0 ? 1 : 0.7)) * s;
                dc.DrawLine(pen, OrbBrushes.Polar(c, inner, angle), OrbBrushes.Polar(c, outer, angle));
            }
        }
    }

    private static Rect Inflate(Rect rect, double amount)
    {
        rect.Inflate(amount, amount);
        return rect;
    }

    private static bool IsLatin(string text)
    {
        foreach (var ch in text)
        {
            if (ch > 0x024F)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class OrbAutomationPeer(OrbView owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => ((OrbView)Owner).AccessibleName;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => "AgentSpeedOrb";

        // The dashboard hero is decorative: its value and title are read from the text beside it.
        protected override bool IsContentElementCore() => !((OrbView)Owner).IsEmbedded;

        protected override bool IsControlElementCore() => !((OrbView)Owner).IsEmbedded;
    }
}
