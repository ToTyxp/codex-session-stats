using System.Runtime.InteropServices;
using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Pipe;

/// <summary>
/// One instance of <c>\\.\pipe\AgentSpeed.Traffic</c> served by its own thread with overlapped I/O (WINDOWS-SERVICE 4):
/// a client that sends nothing for 60 s is disconnected, the rest of a started request must arrive within 2 s, a client
/// that does not read its response within 5 s is disconnected, and stop is noticed immediately. The server identifies
/// the client's session with GetNamedPipeClientSessionId (never impersonating) and serves only sessions the
/// <see cref="SessionGate"/> admits.
/// </summary>
internal sealed unsafe class PipeInstance : IDisposable
{
    private const uint SilentClientMs = 60_000;
    private const uint RequestRestMs = 2_000;
    private const uint WriteTimeoutMs = 5_000;
    private const uint DrainMs = 500;
    private const uint OutBufferSize = 64 * 1024;
    private const uint InBufferSize = 4 * 1024;

    private const int Failed = -1;
    private const int TimedOut = -2;
    private const int Stopped = -3;

    private readonly nint _stopEvent;
    private readonly RequestHandler _handler;
    private readonly IdleTracker _idle;
    private readonly SessionGate _gate;
    private readonly RequestRateLimiter _limiter = new();
    private readonly byte[] _request = new byte[HelperProtocol.RequestSize + 1];
    private byte[] _response = new byte[4 * 1024];
    private nint _pipe;
    private nint _ioEvent;
    private IoOverlapped* _overlapped;

    private PipeInstance(nint pipe, nint ioEvent, nint stopEvent, RequestHandler handler, IdleTracker idle, SessionGate gate)
    {
        _pipe = pipe;
        _ioEvent = ioEvent;
        _stopEvent = stopEvent;
        _handler = handler;
        _idle = idle;
        _gate = gate;
        _overlapped = (IoOverlapped*)NativeMemory.AllocZeroed((nuint)sizeof(IoOverlapped));
    }

    /// <summary>Creates one instance; the first uses FILE_FLAG_FIRST_PIPE_INSTANCE, so a name already taken fails.</summary>
    public static PipeInstance? Create(
        bool first, SecurityDescriptor descriptor, nint stopEvent, RequestHandler handler, IdleTracker idle, SessionGate gate, out uint error)
    {
        var attributes = new SecurityAttributes
        {
            Length = (uint)sizeof(SecurityAttributes),
            SecurityDescriptor = descriptor.Pointer,
        };
        var openMode = Kernel32.PipeAccessDuplex | Kernel32.FileFlagOverlapped | (first ? Kernel32.FileFlagFirstPipeInstance : 0);
        var pipeMode = Kernel32.PipeTypeByte | Kernel32.PipeReadModeByte | Kernel32.PipeWait | Kernel32.PipeRejectRemoteClients;
        var pipe = Kernel32.CreateNamedPipeW(
            HelperProtocol.PipePath, openMode, pipeMode, PipeServer.Instances, OutBufferSize, InBufferSize, 0, &attributes);
        if (pipe == Kernel32.InvalidHandle)
        {
            error = Kernel32.LastError;
            return null;
        }

        var ioEvent = Kernel32.CreateEventW(0, 1, 0, 0);
        if (ioEvent == 0)
        {
            error = Kernel32.LastError;
            Kernel32.CloseHandle(pipe);
            return null;
        }

        error = 0;
        return new PipeInstance(pipe, ioEvent, stopEvent, handler, idle, gate);
    }

    /// <summary>The instance's thread: accept, serve, disconnect, until stop.</summary>
    public void Run()
    {
        while (!IsStopping())
        {
            if (!Accept())
            {
                if (IsStopping())
                {
                    return;
                }

                Thread.Sleep(100);
                continue;
            }

            // Session 0 and a session already holding its share of the instances are disconnected unanswered.
            uint session;
            if (Kernel32.GetNamedPipeClientSessionId(_pipe, &session) != 0 && _gate.TryEnter(session))
            {
                _idle.Connected();
                try
                {
                    Serve(session);
                }
                finally
                {
                    _idle.Disconnected(Environment.TickCount64);
                    _gate.Leave(session);
                }
            }

            Kernel32.DisconnectNamedPipe(_pipe);
        }
    }

    public void Dispose()
    {
        Kernel32.Close(ref _pipe);
        Kernel32.Close(ref _ioEvent);
        if (_overlapped != null)
        {
            NativeMemory.Free(_overlapped);
            _overlapped = null;
        }
    }

    private bool Accept()
    {
        Prepare();
        if (Kernel32.ConnectNamedPipe(_pipe, _overlapped) != 0)
        {
            return true;
        }

        var error = Kernel32.LastError;
        if (error == Win32Error.PipeConnected)
        {
            return true;
        }

        if (error == Win32Error.NoData)
        {
            Kernel32.DisconnectNamedPipe(_pipe);
            return false;
        }

        return error == Win32Error.IoPending && Complete(Kernel32.Infinite) >= 0;
    }

    private void Serve(uint session)
    {
        _limiter.Reset();
        while (true)
        {
            var length = ReadRequest();
            if (length <= 0)
            {
                return;
            }

            var after = _handler.Handle(_request.AsSpan(0, length), session, Environment.TickCount64, _limiter, ref _response, out var size);
            if (!WriteAll(size))
            {
                return;
            }

            if (after == AfterResponse.Close)
            {
                Drain();
                return;
            }
        }
    }

    /// <summary>The bytes of one request (16, fewer after the 2 s timeout, 17 when more followed); 0 or less to disconnect.</summary>
    private int ReadRequest()
    {
        var got = Read(0, HelperProtocol.RequestSize, SilentClientMs);
        if (got <= 0)
        {
            return 0;
        }

        var deadline = Environment.TickCount64 + RequestRestMs;
        while (got < HelperProtocol.RequestSize)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
            {
                break;
            }

            var more = Read(got, HelperProtocol.RequestSize - got, (uint)remaining);
            if (more == TimedOut)
            {
                break;
            }

            if (more <= 0)
            {
                return 0;
            }

            got += more;
        }

        if (got == HelperProtocol.RequestSize)
        {
            uint available = 0;
            if (Kernel32.PeekNamedPipe(_pipe, null, 0, null, &available, null) != 0 && available > 0)
            {
                got = HelperProtocol.RequestSize + 1;
            }
        }

        return got;
    }

    private int Read(int offset, int count, uint timeoutMs)
    {
        Prepare();
        fixed (byte* buffer = _request)
        {
            if (Kernel32.ReadFile(_pipe, buffer + offset, (uint)count, null, _overlapped) == 0 && Kernel32.LastError != Win32Error.IoPending)
            {
                return Failed;
            }

            var read = Complete(timeoutMs);
            // A zero-byte read is a protocol violation, never a reason to spin.
            return read == 0 ? Failed : read;
        }
    }

    private bool WriteAll(int length)
    {
        Prepare();
        fixed (byte* buffer = _response)
        {
            if (Kernel32.WriteFile(_pipe, buffer, (uint)length, null, _overlapped) == 0 && Kernel32.LastError != Win32Error.IoPending)
            {
                return false;
            }

            return Complete(WriteTimeoutMs) == length;
        }
    }

    /// <summary>Gives the client a moment to read an error response before the instance disconnects.</summary>
    private void Drain()
    {
        Prepare();
        var scratch = stackalloc byte[16];
        if (Kernel32.ReadFile(_pipe, scratch, 16, null, _overlapped) != 0 || Kernel32.LastError == Win32Error.IoPending)
        {
            Complete(DrainMs);
        }
    }

    private void Prepare()
    {
        *_overlapped = default;
        _overlapped->EventHandle = _ioEvent;
    }

    /// <summary>Waits for the pending operation; on timeout or stop cancels it and waits for the cancellation to finish.</summary>
    private int Complete(uint timeoutMs)
    {
        var handles = stackalloc nint[2];
        handles[0] = _ioEvent;
        handles[1] = _stopEvent;
        var wait = Kernel32.WaitForMultipleObjects(2, handles, 0, timeoutMs);
        uint transferred;
        if (wait == Kernel32.WaitObject0)
        {
            return Kernel32.GetOverlappedResult(_pipe, _overlapped, &transferred, 0) != 0 ? (int)transferred : Failed;
        }

        Kernel32.CancelIoEx(_pipe, _overlapped);
        var completed = Kernel32.GetOverlappedResult(_pipe, _overlapped, &transferred, 1) != 0;
        if (completed && wait == Kernel32.WaitTimeout && transferred > 0)
        {
            return (int)transferred;
        }

        return wait == Kernel32.WaitTimeout ? TimedOut : Stopped;
    }

    private bool IsStopping() => Kernel32.WaitForSingleObject(_stopEvent, 0) == Kernel32.WaitObject0;
}
