using System.Windows.Threading;
using AgentSpeed.App;
using AgentSpeed.Interop;

namespace AgentSpeed.Taskbar;

/// <summary>
/// UI-thread facade for the taskbar widget. Owns the dedicated taskbar thread and marshals state in and
/// interaction signals out.
/// </summary>
internal sealed class TaskbarWidget : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private TaskbarHost? _host;
    private volatile TaskbarState _latest = TaskbarState.Initial;
    private volatile bool _updateQueued;
    private bool _disposed;

    public TaskbarWidget(Dispatcher ui)
    {
        _ui = ui;
    }

    public event EventHandler<RECT>? Invoked;

    public event EventHandler<POINT>? MenuRequested;

    public event EventHandler<(bool IsOver, RECT Bounds)>? HoverChanged;

    public event EventHandler? PresenceChanged;

    public TaskbarPresence Presence { get; private set; } = TaskbarPresence.Hidden;

    /// <summary>Last known widget rectangle in physical screen pixels.</summary>
    public RECT Bounds { get; private set; }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run)
        {
            Name = "AgentSpeed.Taskbar",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            AppLog.Warn("The taskbar thread did not start in time.");
        }
    }

    public void Update(TaskbarState state)
    {
        // Idle snapshots repeat the same state; do not wake the taskbar thread for them.
        if (state == _latest && _dispatcher is not null)
        {
            return;
        }

        _latest = state;
        if (_updateQueued || _dispatcher is null)
        {
            return;
        }

        _updateQueued = true;
        Post(host =>
        {
            _updateQueued = false;
            host.Apply(_latest);
        });
    }

    public void Reembed() => Post(host => host.Reembed());

    public void Reposition() => Post(host => host.QueueEnsure());

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var dispatcher = _dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        try
        {
            dispatcher.Invoke(() => _host?.Dispose(), DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
            dispatcher.InvokeShutdown();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or TaskCanceledException)
        {
            AppLog.Warn("Taskbar thread shutdown was not clean.", ex);
        }

        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _dispatcher.UnhandledException += (_, e) =>
            {
                AppLog.Error("Unhandled exception on the taskbar thread.", e.Exception);
                e.Handled = true;
            };
            _host = new TaskbarHost(OnSignal);
            _ready.Set();
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            AppLog.Error("The taskbar thread stopped.", ex);
        }
        finally
        {
            _ready.Set();
        }
    }

    private void Post(Action<TaskbarHost> action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || _disposed || dispatcher.HasShutdownStarted)
        {
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (_host is { } host)
            {
                action(host);
            }
        });
    }

    /// <summary>Runs on the taskbar thread; forwards to the UI thread.</summary>
    private void OnSignal(TaskbarSignal signal)
    {
        _ui.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            switch (signal)
            {
                case TaskbarSignal.Invoked invoked:
                    Bounds = invoked.Bounds;
                    Invoked?.Invoke(this, invoked.Bounds);
                    break;
                case TaskbarSignal.MenuRequested menu:
                    MenuRequested?.Invoke(this, menu.Cursor);
                    break;
                case TaskbarSignal.Hover hover:
                    Bounds = hover.Bounds;
                    HoverChanged?.Invoke(this, (hover.IsOver, hover.Bounds));
                    break;
                case TaskbarSignal.PresenceChanged presence:
                    Presence = presence.Presence;
                    Bounds = presence.Bounds;
                    PresenceChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }
        });
    }
}
