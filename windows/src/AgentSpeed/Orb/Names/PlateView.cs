using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AgentSpeed.Presentation.Orb.Names;

namespace AgentSpeed.Orb.Names;

/// <summary>
/// The plate drawn with retained visuals: a body (capsule with its shadow) re-recorded only when its width or look
/// changes, and two cached content drawings (previous and current) that crossfade. Never a UI Automation element: the
/// orb's accessible name already begins with its name.
/// </summary>
internal sealed class PlateView : FrameworkElement
{
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _body = new();
    private readonly ContainerVisual _content = new();
    private readonly DrawingVisual _previous = new();
    private readonly DrawingVisual _current = new();
    private readonly TranslateTransform _previousOffset = new();
    private readonly TranslateTransform _currentOffset = new();
    private readonly TranslateTransform _motion = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DropShadowEffect _shadow = new()
    {
        Color = Colors.Black,
        Direction = 270,
        ShadowDepth = Presentation.Orb.Names.NamePlateMaterial.ShadowOffsetY,
        BlurRadius = 9,
        RenderingBias = RenderingBias.Quality,
    };

    private PlateInk? _ink;
    private Rect _bodyRect = Rect.Empty;
    private double _previousWidth;
    private double _currentWidth;

    public PlateView()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = false;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        _previous.Transform = _previousOffset;
        _current.Transform = _currentOffset;
        _content.Children.Add(_previous);
        _content.Children.Add(_current);
        _root.Children.Add(_body);
        _root.Children.Add(_content);
        var transforms = new TransformGroup();
        transforms.Children.Add(_scale);
        transforms.Children.Add(_motion);
        _root.Transform = transforms;
        AddVisualChild(_root);
    }

    /// <summary>The capsule's rectangle in this view (before the motion transform).</summary>
    public Rect Capsule => _bodyRect;

    /// <summary>The motion offset applied to the whole plate (pt).</summary>
    public Vector MotionOffset => new(_motion.X, _motion.Y);

    /// <summary>The capsule as the pointer sees it (motion included).</summary>
    public bool HitCapsule(Point point)
    {
        if (_bodyRect.IsEmpty || !_root.Transform.Inverse.TryTransform(point, out var local))
        {
            return false;
        }

        var r = _bodyRect.Height / 2;
        var x = Math.Clamp(local.X, _bodyRect.Left + r, _bodyRect.Right - r);
        var dx = local.X - x;
        var dy = local.Y - (_bodyRect.Top + r);
        return dx * dx + dy * dy <= r * r + 0.5;
    }

    public double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    /// <summary>A new current content; the old one becomes the previous (crossfading out).</summary>
    public void SetContent(NamePlateLayout layout, PlateInk ink, PlateTypography type, bool keepPrevious)
    {
        if (keepPrevious)
        {
            using (var dc = _previous.RenderOpen())
            {
                if (_current.Drawing is { } drawing)
                {
                    dc.DrawDrawing(drawing);
                }
            }

            _previousWidth = _currentWidth;
        }
        else
        {
            using (_previous.RenderOpen())
            {
            }

            _previousWidth = 0;
        }

        using (var dc = _current.RenderOpen())
        {
            PlatePainter.Content(dc, layout, ink, type, PixelsPerDip);
        }

        _currentWidth = layout.Width;
        UpdateOffsets();
    }

    /// <summary>Draws the capsule at <paramref name="capsule"/> (re-recorded only when it or the look changes).</summary>
    public void SetBody(Rect capsule, PlateInk ink)
    {
        if (ReferenceEquals(ink, _ink) && capsule == _bodyRect)
        {
            return;
        }

        if (!ReferenceEquals(ink, _ink))
        {
            _ink = ink;
            _shadow.Opacity = ink.ShadowOpacity;
            _body.Effect = ink.ShadowOpacity > 0 ? _shadow : null;
        }

        _bodyRect = capsule;
        using (var dc = _body.RenderOpen())
        {
            PlatePainter.Body(dc, capsule, ink);
        }

        _content.Clip = new RectangleGeometry(capsule, capsule.Height / 2, capsule.Height / 2);
        UpdateOffsets();
    }

    /// <summary>Both contents stay centered in the capsule while its width springs.</summary>
    private void UpdateOffsets()
    {
        if (_bodyRect.IsEmpty)
        {
            return;
        }

        _previousOffset.X = _bodyRect.X + (_bodyRect.Width - _previousWidth) / 2;
        _previousOffset.Y = _bodyRect.Y;
        _currentOffset.X = _bodyRect.X + (_bodyRect.Width - _currentWidth) / 2;
        _currentOffset.Y = _bodyRect.Y;
    }

    /// <summary>The crossfade: 0 shows the previous content, 1 the current one.</summary>
    public void SetBlend(double blend)
    {
        var b = Math.Clamp(blend, 0, 1);
        _current.Opacity = b;
        _previous.Opacity = 1 - b;
    }

    /// <summary>Motion of the whole plate: an offset (pt) and a scale about the capsule's center.</summary>
    public void SetMotion(Vector offset, double scale)
    {
        _motion.X = offset.X;
        _motion.Y = offset.Y;
        if (_scale.ScaleX != scale)
        {
            _scale.CenterX = _bodyRect.IsEmpty ? 0 : _bodyRect.X + _bodyRect.Width / 2;
            _scale.CenterY = _bodyRect.IsEmpty ? 0 : _bodyRect.Y + _bodyRect.Height / 2;
            _scale.ScaleX = scale;
            _scale.ScaleY = scale;
        }
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => index == 0 ? _root : throw new ArgumentOutOfRangeException(nameof(index));

    protected override System.Windows.Automation.Peers.AutomationPeer? OnCreateAutomationPeer() => null;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        DisplayDpiChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The display's DPI changed: the content is rebuilt so its baseline stays on whole pixels.</summary>
    public event EventHandler? DisplayDpiChanged;
}
