using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace AgentSpeed.Settings.Pages;

/// <summary>
/// Settings → API (DESIGN 11). Text fields commit on Return or when they lose focus (Esc restores); the status line is a
/// polite live region announced at most once a minute, except a change to a state that needs the user.
/// </summary>
internal sealed partial class ReportPage
{
    private readonly DispatcherTimer _clock;
    private ReportPaneViewModel? _model;
    private string? _announced;
    private long _announcedAt = long.MinValue;
    private bool _committingKey;

    public ReportPage()
    {
        InitializeComponent();
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => _model?.Tick(), Dispatcher);
        _clock.Stop();
        DataContextChanged += (_, _) => Attach(DataContext as ReportPaneViewModel);
        Loaded += (_, _) => { if (IsVisible) { _clock.Start(); _model?.Opened(); } };
        Unloaded += (_, _) => { _clock.Stop(); _model?.Closed(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _clock.Start();
                _model?.Opened();
                UpdateApiKeyPlaceholder();
            }
            else
            {
                _clock.Stop();
                _model?.Closed();
            }
        };
    }

    private void Attach(ReportPaneViewModel? model)
    {
        if (_model is not null)
        {
            _model.Closed();
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = model;
        if (model is not null)
        {
            model.PropertyChanged += OnModelChanged;
            if (IsVisible) model.Opened();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            UpdateApiKeyPlaceholder();
        }

        if (_model is null || !(string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ReportPaneViewModel.StatusText)))
        {
            return;
        }

        var text = _model.StatusText;
        var now = Environment.TickCount64;
        if (text == _announced || (!_model.StatusNeedsUser && now - _announcedAt < 60_000))
        {
            return;
        }

        _announced = text;
        _announcedAt = now;
        if (UIElementAutomationPeer.FromElement(StatusLine) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void OnUrlKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _model?.CommitUrl();
            e.Handled = true;
        }
    }

    private void OnUrlLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _model?.CommitUrl();

    private void OnUsernameGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_model is not null)
        {
            _model.UsernameEditing = true;
        }
    }

    private void OnUsernameKeyDown(object sender, KeyEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _model.CommitUsername();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _model.RestoreUsername();
            UsernameBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnUsernameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_model is null)
        {
            return;
        }

        _model.UsernameEditing = false;
        _model.CommitUsername();
    }

    private void OnApiKeyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveApiKey();
            e.Handled = true;
        }
    }

    private void OnApiKeyLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (ApiKeyBox.Password.Length > 0)
        {
            SaveApiKey();
        }
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e) => UpdateApiKeyPlaceholder();

    private void SaveApiKey()
    {
        if (_model is null || _committingKey)
        {
            return;
        }

        _committingKey = true;
        try
        {
            if (_model.SaveApiKey(ApiKeyBox.Password))
            {
                ApiKeyBox.Clear();
            }
        }
        finally
        {
            _committingKey = false;
            UpdateApiKeyPlaceholder();
        }
    }

    private void UpdateApiKeyPlaceholder()
    {
        ApiKeyPlaceholder.Visibility = ApiKeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyEntry.Visibility = _model?.HasSavedApiKey == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.DataContext = DataContext;
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }
}
