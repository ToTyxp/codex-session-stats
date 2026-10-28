using System.Windows;
using System.Windows.Threading;
using AgentSpeed.App;
using AgentSpeed.Presentation.Settings;
using Microsoft.Win32;

namespace AgentSpeed.Theme;

/// <summary>Effective light/dark state for windows and for the taskbar, plus accessibility preferences.</summary>
internal sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Dispatcher _dispatcher;
    private bool _refreshQueued;
    private AppearanceMode _mode = AppearanceMode.Auto;

    private readonly bool _forceReduceMotion;
    private bool _fluentEnabled;

    /// <param name="forceReduceMotion">Development flag <c>--reduce-motion</c>: act as if system animations were off.</param>
    public ThemeService(Dispatcher dispatcher, AppearanceMode mode, bool forceReduceMotion = false)
    {
        _dispatcher = dispatcher;
        _mode = mode;
        _forceReduceMotion = forceReduceMotion;
        ReadSystem();
        IsDark = Resolve(_mode);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? Changed;

    public AppearanceMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            Apply(force: true);
        }
    }

    /// <summary>App windows and the orb use dark colors.</summary>
    public bool IsDark { get; private set; }

    /// <summary>The taskbar itself is dark (the widget text must contrast with it, whatever the app theme).</summary>
    public bool TaskbarIsDark { get; private set; } = true;

    public bool HighContrast { get; private set; }

    public bool ReduceMotion { get; private set; }

    public bool TransparencyEnabled { get; private set; } = true;

    private bool SystemAppsDark { get; set; }

    /// <summary>
    /// Loads the Fluent theme. Parsing its dictionaries is the most expensive part of startup, so the app calls this
    /// right after its first surfaces are up (the orb and the taskbar widget do not depend on it).
    /// </summary>
    public void EnableFluent()
    {
        _fluentEnabled = true;
        Apply(force: true);
    }

    /// <summary>Coalesces bursts of WM_SETTINGCHANGE into one refresh.</summary>
    public void QueueRefresh()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Background, QueueRefresh);
            return;
        }

        if (_refreshQueued)
        {
            return;
        }

        _refreshQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _refreshQueued = false;
            Apply(force: false);
        });
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
            or UserPreferenceCategory.Accessibility or UserPreferenceCategory.VisualStyle)
        {
            QueueRefresh();
        }
    }

    private void Apply(bool force)
    {
        var before = (IsDark, TaskbarIsDark, HighContrast, ReduceMotion, TransparencyEnabled);
        ReadSystem();
        IsDark = Resolve(_mode);

        var after = (IsDark, TaskbarIsDark, HighContrast, ReduceMotion, TransparencyEnabled);
        if (!force && before == after)
        {
            return;
        }

        ApplyFluentTheme();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool Resolve(AppearanceMode mode) => mode switch
    {
        AppearanceMode.Dark => true,
        AppearanceMode.Light => false,
        _ => SystemAppsDark,
    };

    private void ApplyFluentTheme()
    {
        var application = Application.Current;
        if (application is null || !_fluentEnabled)
        {
            return;
        }

        try
        {
            var desired = IsDark ? ThemeMode.Dark : ThemeMode.Light;
            if (application.ThemeMode != desired)
            {
                application.ThemeMode = desired;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or System.Windows.Markup.XamlParseException)
        {
            AppLog.Warn("Could not apply the Fluent theme.", ex);
        }
    }

    private void ReadSystem()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            SystemAppsDark = ReadDword(key, "AppsUseLightTheme", 1) == 0;
            TaskbarIsDark = ReadDword(key, "SystemUsesLightTheme", 0) == 0;
            TransparencyEnabled = ReadDword(key, "EnableTransparency", 1) != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            SystemAppsDark = false;
            TaskbarIsDark = true;
            TransparencyEnabled = true;
        }

        HighContrast = SystemParameters.HighContrast;
        ReduceMotion = _forceReduceMotion || !SystemParameters.ClientAreaAnimation;
    }

    private static int ReadDword(RegistryKey? key, string name, int fallback) =>
        key?.GetValue(name) is int value ? value : fallback;
}
