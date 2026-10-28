using System.Globalization;
using System.Windows.Threading;
using AgentSpeed.App;
using AgentSpeed.Core;
using AgentSpeed.Core.Traffic;
using AgentSpeed.Core.Traffic.Monitor;
using AgentSpeed.Presentation.Settings;
using AgentSpeed.Services;
using Microsoft.Win32;

namespace AgentSpeed.Traffic;

/// <summary>
/// Runs network traffic measuring for the app (METRICS 10): starts and stops the monitor with the setting, hands it the
/// agents of every snapshot, tells it when a visible surface shows per-source or per-agent traffic (so it samples every
/// second), and writes today's totals before sleep. UI thread, except where noted.
/// </summary>
internal sealed class TrafficService : IDisposable
{
    private readonly SettingsService _settings;
    private TrafficMonitor? _monitor;
    private bool _demand;
    private bool _disposed;
    private TrafficSnapshot? _visibleFrom;
    private SourceSet _visibleFor;
    private TrafficSnapshot _visible = TrafficSnapshot.Off;

    public TrafficService(Dispatcher dispatcher, SettingsService settings)
    {
        _settings = settings;
        Hub = new TrafficHub(new DispatcherSynchronizationContext(dispatcher));
    }

    public TrafficHub Hub { get; }

    /// <summary>
    /// The live traffic as every surface shows it: sources turned off in Settings → Data Sources left out (METRICS 10.5,
    /// DESIGN 10). Recomputed only when the hub's snapshot or the enabled sources change.
    /// </summary>
    public TrafficSnapshot Snapshot
    {
        get
        {
            var raw = Hub.Snapshot;
            var enabled = _settings.Current.EnabledSourceSet();
            if (!ReferenceEquals(raw, _visibleFrom) || enabled != _visibleFor)
            {
                _visibleFrom = raw;
                _visibleFor = enabled;
                _visible = TrafficVisibility.Snapshot(raw, enabled);
            }

            return _visible;
        }
    }

    /// <summary>Installs and removes the helper service; null where it does not apply.</summary>
    public ITrafficHelperHost? Helper { get; set; }

    /// <summary>The monitor runs (it failed to start, or the app is not on Windows, otherwise).</summary>
    public bool IsRunning => _monitor is not null;

    public void Start()
    {
        if (_monitor is not null || _disposed)
        {
            return;
        }

        try
        {
            _monitor = TrafficMonitor.CreateForWindows(Hub, log: message => AppLog.Info(message));
            _monitor.Start(_settings.Current.Traffic.Enabled);
            // Measurements: AGENT_SPEED_TRAFFIC_INTERVAL=1|5 fixes the per-process interval (0: standby).
            if (Environment.GetEnvironmentVariable("AGENT_SPEED_TRAFFIC_INTERVAL") is { Length: > 0 } fixedInterval
                && int.TryParse(fixedInterval, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 0 and <= 60)
            {
                _monitor.SetForcedInterval(seconds);
                AppLog.Info($"Traffic: per-process interval fixed at {seconds} s.");
            }

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            AppLog.Error("Network traffic measuring failed to start.", ex);
            _monitor = null;
        }
    }

    public void OnSettingsChanged(AppSettings previous, AppSettings current)
    {
        if (previous.Traffic.Enabled != current.Traffic.Enabled)
        {
            _monitor?.SetEnabled(current.Traffic.Enabled);
        }
    }

    /// <summary>The agents of every real speed snapshot (never the effects preview's made-up ones).</summary>
    public void UpdateAgents(IReadOnlyList<AgentStatus> agents) => _monitor?.UpdateAgents(agents);

    /// <summary>METRICS 10.10: true while the dashboard, the taskbar's agents block or an orb's sources readout shows per-source traffic.</summary>
    public void SetDemand(bool shown)
    {
        if (shown == _demand)
        {
            return;
        }

        _demand = shown;
        _monitor?.SetDetailDemand(shown);
    }

    /// <summary>Settings → Retry, or after the helper was installed, updated or removed.</summary>
    public void Retry() => _monitor?.Retry();

    /// <summary>Call right before the history store is deleted ("Clear history").</summary>
    public void ClearHistory() => _monitor?.ClearHistory();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_monitor is not null)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _monitor.Dispose();
            _monitor = null;
        }
    }

    /// <summary>Any thread: the system is about to sleep; today's totals are written first (METRICS 10.9).</summary>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _monitor?.Flush();
        }
    }
}
