using System.Windows.Threading;
using AgentSpeed.Core;

namespace AgentSpeed.Services;

/// <summary>Runs the 20 s effects preview (DemoDriver) and reports its state on the UI thread.</summary>
internal sealed class PreviewService : IDisposable
{
    private readonly DemoDriver _driver;
    private readonly Dispatcher _dispatcher;

    public PreviewService(SpeedHub hub, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _driver = new DemoDriver(hub);
        _driver.Finished += (_, _) => _dispatcher.BeginInvoke(() =>
        {
            var running = _driver.IsRunning;
            if (running == IsRunning)
            {
                return;
            }

            IsRunning = running;
            StateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public event EventHandler? StateChanged;

    public bool IsRunning { get; private set; }

    public void Start(double redline)
    {
        _driver.Start(redline, TimeSpan.FromSeconds(20));
        IsRunning = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _driver.Stop();
        if (IsRunning)
        {
            IsRunning = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => _driver.Dispose();
}
