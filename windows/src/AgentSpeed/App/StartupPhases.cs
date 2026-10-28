using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AgentSpeed.App;

/// <summary>Startup timing for the log: time from process start to Main, then each phase of AppController.Start.</summary>
internal sealed class StartupPhases
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StringBuilder _text = new();
    private long _last;

    public static long ProcessToMainMs { get; private set; } = -1;

    public static Stopwatch SinceMain { get; } = new();

    /// <summary>Called first thing in Main.</summary>
    public static void MarkMain()
    {
        SinceMain.Start();
        try
        {
            using var process = Process.GetCurrentProcess();
            ProcessToMainMs = (long)(DateTime.Now - process.StartTime).TotalMilliseconds;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            ProcessToMainMs = -1;
        }
    }

    public void Mark(string phase)
    {
        var now = _clock.ElapsedMilliseconds;
        _text.Append(phase).Append(' ').Append((now - _last).ToString(CultureInfo.InvariantCulture)).Append(" ms, ");
        _last = now;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"Startup: process→Main {ProcessToMainMs} ms, Main→ready {SinceMain.ElapsedMilliseconds} ms ({_text}start total {_clock.ElapsedMilliseconds} ms).");
}
