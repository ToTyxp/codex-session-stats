using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AgentSpeed.Interop;

namespace AgentSpeed.Orb.Names;

/// <summary>
/// The window of a plate: owned by its orb (so it stays in front of it, above the aura, sparks and the Settings
/// highlight ring), layered, never activated, placed in physical pixels. Only the capsule takes the pointer: elsewhere
/// the window lets clicks through (<c>WS_EX_TRANSPARENT</c> toggled by the cursor's position, as on the orb), and with a
/// click-through orb it ignores the mouse entirely. Its opacity follows the orb window's (the unavailable dim and the
/// idle fade).
/// </summary>
internal class PlateWindow : Window
{
    private readonly DispatcherTimer _poll;
    private readonly bool _hiddenFromAutomation;
    private nint _hwnd;
    private bool _clickThrough;
    private bool _passThrough;
    private bool _inside;

    /// <param name="overlay">Interactive content above the plate (the undo chip's action), or null.</param>
    public PlateWindow(IOrbWindow orb, UIElement? overlay = null)
    {
        Orb = orb;
        View = new PlateView();
        _hiddenFromAutomation = overlay is null;
        if (overlay is null)
        {
            Content = View;
        }
        else
        {
            var grid = new System.Windows.Controls.Grid();
            grid.Children.Add(View);
            grid.Children.Add(overlay);
            Content = grid;
        }

        Title = "Agent Speed Orb Plate";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        Focusable = false;
        Style = new Style(typeof(Window));
        UseLayoutRounding = false;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;
        orb.OwnPlate(this);
        Topmost = orb.Options.AlwaysOnTop;
        if (orb is Window owner)
        {
            SetBinding(OpacityProperty, new Binding(nameof(Opacity)) { Source = owner, Mode = BindingMode.OneWay });
        }
        else
        {
            Opacity = orb.Opacity;
            orb.OpacityChanged += OnOrbOpacityChanged;
        }
        _poll = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => Poll(), Dispatcher);
        _poll.Stop();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowEffects.AddExtendedStyle(_hwnd, WS_EX.TOOLWINDOW | WS_EX.NOACTIVATE);
            ApplyTransparentStyle(_clickThrough);
        };
        Closed += (_, _) =>
        {
            IsClosed = true;
            _poll.Stop();
            orb.OpacityChanged -= OnOrbOpacityChanged;
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _poll.Stop();
                if (_passThrough)
                {
                    _passThrough = false;
                    ApplyTransparentStyle(_clickThrough);
                }

                SetInside(false);
            }
        };
    }

    private void OnOrbOpacityChanged(object? sender, EventArgs e)
    {
        if (!IsClosed && Math.Abs(Opacity - Orb.Opacity) > 0.0005)
        {
            Opacity = Orb.Opacity;
        }
    }

    /// <summary>Closed (by its plate, or with its owner orb).</summary>
    public bool IsClosed { get; private set; }

    public IOrbWindow Orb { get; }

    public PlateView View { get; }

    public nint Handle => IsClosed ? 0 : _hwnd;

    /// <summary>The pointer is over the capsule.</summary>
    public bool IsPointerInside => _inside;

    /// <summary>Follows the orb's always-on-top and click-through options (the undo chip passes click-through off).</summary>
    public void Follow(OrbWindowOptions options)
    {
        if (IsClosed)
        {
            return;
        }

        if (Topmost != options.AlwaysOnTop)
        {
            Topmost = options.AlwaysOnTop;
        }

        if (_clickThrough != options.ClickThrough)
        {
            _clickThrough = options.ClickThrough;
            _passThrough = false;
            _poll.Stop();
            ApplyTransparentStyle(_clickThrough);
            SetInside(false);
        }
    }

    /// <summary>Shows the window if needed and moves it to a physical-pixel rectangle.</summary>
    public void Place(PixelRect rect)
    {
        if (IsClosed)
        {
            return;
        }

        var dips = Math.Max(0.25, View.PixelsPerDip);
        Width = Math.Max(1, rect.Width / dips);
        Height = Math.Max(1, rect.Height / dips);
        if (!IsVisible)
        {
            Show();
        }

        if (_hwnd != 0)
        {
            User32.SetWindowPos(_hwnd, 0, rect.Left, rect.Top, Math.Max(1, rect.Width), Math.Max(1, rect.Height),
                SWP.NOZORDER | SWP.NOACTIVATE | SWP.NOOWNERZORDER);
        }
    }

    /// <summary>The pointer entered or left the capsule.</summary>
    protected virtual void OnPointerInside(bool inside)
    {
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        _hiddenFromAutomation ? new HiddenPeer(this) : base.OnCreateAutomationPeer();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            return;
        }

        var inside = View.HitCapsule(e.GetPosition(View));
        SetInside(inside);
        if (!inside)
        {
            SetPassThrough(true);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!IsMouseCaptured)
        {
            SetInside(false);
        }
    }

    protected void SetInside(bool inside)
    {
        if (_inside != inside)
        {
            _inside = inside;
            OnPointerInside(inside);
        }
    }

    private void Poll()
    {
        if (!_passThrough || !IsVisible || _hwnd == 0 || !User32.GetCursorPos(out var cursor) || !User32.GetWindowRect(_hwnd, out var rect))
        {
            _poll.Stop();
            return;
        }

        if (cursor.X < rect.Left || cursor.X >= rect.Right || cursor.Y < rect.Top || cursor.Y >= rect.Bottom)
        {
            SetPassThrough(false);
            return;
        }

        var scale = rect.Width / Math.Max(1, ActualWidth);
        if (View.HitCapsule(new Point((cursor.X - rect.Left) / scale, (cursor.Y - rect.Top) / scale)))
        {
            SetPassThrough(false);
            SetInside(true);
        }
    }

    private void SetPassThrough(bool enabled)
    {
        if (_clickThrough || _passThrough == enabled)
        {
            return;
        }

        _passThrough = enabled;
        ApplyTransparentStyle(enabled);
        if (enabled)
        {
            _poll.Start();
        }
        else
        {
            _poll.Stop();
        }
    }

    private void ApplyTransparentStyle(bool transparent)
    {
        if (_hwnd == 0)
        {
            return;
        }

        if (transparent)
        {
            WindowEffects.AddExtendedStyle(_hwnd, WS_EX.TRANSPARENT | WS_EX.LAYERED, frameChanged: false);
        }
        else
        {
            WindowEffects.AddExtendedStyle(_hwnd, 0, WS_EX.TRANSPARENT, frameChanged: false);
        }
    }

    /// <summary>Keeps the plate out of the control and content views: Narrator reads the orb, whose name comes first.</summary>
    private sealed class HiddenPeer(Window owner) : WindowAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;

        protected override List<AutomationPeer> GetChildrenCore() => [];
    }
}
