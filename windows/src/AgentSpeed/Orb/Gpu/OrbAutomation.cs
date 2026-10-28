using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using AgentSpeed.Interop;

namespace AgentSpeed.Orb.Gpu;

/// <summary>
/// UI Automation for a composition orb window (DESIGN 2b, Accessibility): the window reads as a text element named with the
/// orb's speed and title (as the WPF orb's automation peer did) and exposes the orb's actions — open the dashboard, show the
/// menu, move to the next corner, orb settings — as invokable buttons. Calls arrive on the UI thread (COM threading).
/// </summary>
internal sealed class OrbAutomation : IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot
{
    private readonly Func<nint> _hwnd;
    private readonly List<ActionElement> _actions = [];
    private string _name = string.Empty;

    public OrbAutomation(Func<nint> hwnd)
    {
        _hwnd = hwnd;
    }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            var old = _name;
            _name = value;
            if (AutomationInteropProvider.ClientsAreListening && _hwnd() != 0)
            {
                AutomationInteropProvider.RaiseAutomationPropertyChangedEvent(this,
                    new AutomationPropertyChangedEventArgs(AutomationElementIdentifiers.NameProperty, old, value));
            }
        }
    }

    /// <summary>Reads <paramref name="text"/> once (a unit switch, DESIGN 9 Accessibility) through a UI Automation notification.</summary>
    public void Announce(string text)
    {
        if (string.IsNullOrEmpty(text) || _hwnd() == 0 || !AutomationInteropProvider.ClientsAreListening)
        {
            return;
        }

        AutomationInteropProvider.RaiseAutomationEvent(AutomationElementIdentifiers.NotificationEvent, this,
            new NotificationEventArgs(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, text, "unitChanged"));
    }

    public void SetActions(IReadOnlyList<(string Name, Action Invoke)> actions)
    {
        _actions.Clear();
        for (var i = 0; i < actions.Count; i++)
        {
            _actions.Add(new ActionElement(this, i, actions[i].Name, actions[i].Invoke));
        }
    }

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple? HostRawElementProvider => _hwnd() is var hwnd and not 0 ? AutomationInteropProvider.HostProviderFromHandle(hwnd) : null;

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId)
    {
        if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
        {
            return _name;
        }

        if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
        {
            return ControlType.Text.Id;
        }

        if (propertyId == AutomationElementIdentifiers.ClassNameProperty.Id)
        {
            return "AgentSpeedOrb";
        }

        if (propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
        {
            return false;
        }

        return null;
    }

    public Rect BoundingRectangle => Rect.Empty;

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

    public int[]? GetRuntimeId() => null;

    public IRawElementProviderFragment? Navigate(NavigateDirection direction) => direction switch
    {
        NavigateDirection.FirstChild => _actions.Count > 0 ? _actions[0] : null,
        NavigateDirection.LastChild => _actions.Count > 0 ? _actions[^1] : null,
        _ => null,
    };

    public void SetFocus()
    {
    }

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y) => this;

    public IRawElementProviderFragment? GetFocus() => null;

    private Rect CenterPixel()
    {
        var hwnd = _hwnd();
        if (hwnd == 0 || !User32.GetWindowRect(hwnd, out var rect))
        {
            return Rect.Empty;
        }

        return new Rect((rect.Left + rect.Right) / 2.0, (rect.Top + rect.Bottom) / 2.0, 1, 1);
    }

    /// <summary>One orb action as an invokable button.</summary>
    private sealed class ActionElement(OrbAutomation root, int index, string name, Action invoke)
        : IRawElementProviderSimple, IRawElementProviderFragment, IInvokeProvider
    {
        public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

        public IRawElementProviderSimple? HostRawElementProvider => null;

        public object? GetPatternProvider(int patternId) => patternId == InvokePatternIdentifiers.Pattern.Id ? this : null;

        public object? GetPropertyValue(int propertyId)
        {
            if (propertyId == AutomationElementIdentifiers.NameProperty.Id)
            {
                return name;
            }

            if (propertyId == AutomationElementIdentifiers.ControlTypeProperty.Id)
            {
                return ControlType.Button.Id;
            }

            if (propertyId == AutomationElementIdentifiers.IsKeyboardFocusableProperty.Id)
            {
                return false;
            }

            if (propertyId == AutomationElementIdentifiers.IsInvokePatternAvailableProperty.Id)
            {
                return true;
            }

            return null;
        }

        public Rect BoundingRectangle => root.CenterPixel();

        public IRawElementProviderFragmentRoot FragmentRoot => root;

        public IRawElementProviderSimple[]? GetEmbeddedFragmentRoots() => null;

        public int[] GetRuntimeId() => [AutomationInteropProvider.AppendRuntimeId, index + 1];

        public IRawElementProviderFragment? Navigate(NavigateDirection direction) => direction switch
        {
            NavigateDirection.Parent => root,
            NavigateDirection.NextSibling => index + 1 < root._actions.Count ? root._actions[index + 1] : null,
            NavigateDirection.PreviousSibling => index > 0 ? root._actions[index - 1] : null,
            _ => null,
        };

        public void SetFocus()
        {
        }

        public void Invoke() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(invoke);
    }
}
