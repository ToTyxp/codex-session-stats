using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AgentSpeed.Orb.Names;

/// <summary>
/// The name plate's window: the plate belongs to its orb, so hovering it hovers the orb (scale, info line, chip),
/// dragging it drags the orb, right-click opens the orb's menu and a double-click opens the dashboard.
/// </summary>
internal sealed class NamePlateWindow : PlateWindow
{
    public NamePlateWindow(IOrbWindow orb)
        : base(orb)
    {
        Cursor = Cursors.SizeAll;
    }

    protected override void OnPointerInside(bool inside) => Orb.SetPlateHover(inside);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!View.HitCapsule(e.GetPosition(View)))
        {
            return;
        }

        Orb.PlatePress(e.ClickCount);
        if (Orb.IsDragging)
        {
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured)
        {
            Orb.PlateMove();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }

        Orb.PlateRelease();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Orb.PlateRelease();
        SetInside(IsVisible && View.HitCapsule(Mouse.GetPosition(View)));
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (View.HitCapsule(e.GetPosition(View)))
        {
            Orb.PlateMenu();
            e.Handled = true;
        }
    }
}

/// <summary>
/// The undo chip stacked beyond an orb's name plate (DESIGN 2b, Chip and undo): the plate's metrics and material with
/// <c>orbs.undo</c> in the accent colour. The action is a real (invisible) button over that text, so it can be clicked
/// and UI Automation can invoke it.
/// </summary>
internal sealed class UndoPlateWindow : PlateWindow
{
    public UndoPlateWindow(IOrbWindow orb, Button action)
        : base(orb, Overlay(action))
    {
        Action = action;
    }

    public Button Action { get; }

    /// <summary>A transparent, focus-less button: the plate's drawing shows the action; this only takes the click.</summary>
    public static Button CreateAction(string name, string help, Action invoke)
    {
        var face = new FrameworkElementFactory(typeof(Border));
        face.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var button = new Button
        {
            Template = new ControlTemplate(typeof(Button)) { VisualTree = face },
            Focusable = false,
            IsTabStop = false,
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, help);
        button.Click += (_, _) => invoke();
        return button;
    }

    /// <summary>Puts the action over the accent text (window DIP).</summary>
    public void PlaceAction(Rect rect)
    {
        if (Canvas.GetLeft(Action) != rect.X || Canvas.GetTop(Action) != rect.Y || Action.Width != rect.Width || Action.Height != rect.Height)
        {
            Canvas.SetLeft(Action, rect.X);
            Canvas.SetTop(Action, rect.Y);
            Action.Width = Math.Max(0, rect.Width);
            Action.Height = Math.Max(0, rect.Height);
        }
    }

    private static Canvas Overlay(Button action)
    {
        var canvas = new Canvas();
        canvas.Children.Add(action);
        return canvas;
    }
}
