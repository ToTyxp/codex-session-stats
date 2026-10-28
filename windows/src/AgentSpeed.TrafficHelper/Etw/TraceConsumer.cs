using System.Runtime.InteropServices;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Etw;

/// <summary>The real-time consumer: OpenTrace on the session and ProcessTrace on a dedicated thread until the session stops.</summary>
internal sealed unsafe class TraceConsumer : IDisposable
{
    private static TraceSink? s_sink;

    private ulong _trace = Advapi32.InvalidProcessTraceHandle;
    private Thread? _thread;
    private char* _name;
    private EventTraceLogfile* _logfile;
    private volatile bool _running;

    public bool IsRunning => _running;

    /// <summary>Opens the consumer and starts its thread; returns a Win32 error (0 on success).</summary>
    public uint Start(TraceSink sink)
    {
        s_sink = sink;
        _name = (char*)Marshal.StringToHGlobalUni(HelperProtocol.EtwSessionName);
        _logfile = (EventTraceLogfile*)NativeMemory.AllocZeroed((nuint)sizeof(EventTraceLogfile));
        _logfile->LoggerName = _name;
        _logfile->ProcessTraceMode = EventTraceLogfile.ProcessTraceModeRealTime | EventTraceLogfile.ProcessTraceModeEventRecord;
        _logfile->EventRecordCallback = &OnEvent;
        _trace = Advapi32.OpenTraceW(_logfile);
        if (_trace == Advapi32.InvalidProcessTraceHandle)
        {
            var error = Kernel32.LastError;
            Free();
            return error == 0 ? Win32Error.InvalidParameter : error;
        }

        _running = true;
        _thread = new Thread(Pump, 256 * 1024) { IsBackground = true, Name = "etw" };
        _thread.Start();
        return 0;
    }

    /// <summary>Call after the session was stopped: closes the trace and waits briefly for the thread.</summary>
    public void Stop(int waitMs)
    {
        if (_trace != Advapi32.InvalidProcessTraceHandle)
        {
            Advapi32.CloseTrace(_trace);
            _trace = Advapi32.InvalidProcessTraceHandle;
        }

        if (_thread is { } thread && thread.Join(waitMs))
        {
            _thread = null;
            Free();
        }
    }

    public void Dispose() => Stop(1_000);

    private void Pump()
    {
        try
        {
            var trace = _trace;
            Advapi32.ProcessTrace(&trace, 1, null, null);
        }
        finally
        {
            _running = false;
        }
    }

    private void Free()
    {
        if (_logfile != null)
        {
            NativeMemory.Free(_logfile);
            _logfile = null;
        }

        if (_name != null)
        {
            Marshal.FreeHGlobal((nint)_name);
            _name = null;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(EventRecord* record)
    {
        try
        {
            s_sink?.Handle(record);
        }
        catch (Exception)
        {
            // Never let an exception cross into ProcessTrace; a malformed event is simply skipped.
        }
    }
}
