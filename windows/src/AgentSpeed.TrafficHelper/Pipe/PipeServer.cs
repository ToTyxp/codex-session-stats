using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Pipe;

/// <summary>Up to four pipe instances, each on its own thread; the first fails when another process owns the name.</summary>
internal sealed class PipeServer(SecurityDescriptor descriptor, RequestHandler handler, IdleTracker idle, nint stopEvent) : IDisposable
{
    public const uint Instances = 4;

    private readonly List<PipeInstance> _instances = [];
    private readonly List<Thread> _threads = [];
    private readonly SessionGate _gate = new();

    /// <summary>Creates the instances and starts their threads; a Win32 error when the first instance cannot be created.</summary>
    public uint Start()
    {
        for (var i = 0; i < Instances; i++)
        {
            var instance = PipeInstance.Create(i == 0, descriptor, stopEvent, handler, idle, _gate, out var error);
            if (instance is null)
            {
                if (i == 0)
                {
                    return error;
                }

                break;
            }

            _instances.Add(instance);
        }

        foreach (var instance in _instances)
        {
            var thread = new Thread(instance.Run, 256 * 1024) { IsBackground = true, Name = "pipe" };
            _threads.Add(thread);
            thread.Start();
        }

        return 0;
    }

    /// <summary>The stop event must already be set; waits for the threads up to <paramref name="waitMs"/> in total.</summary>
    public void Stop(int waitMs)
    {
        var deadline = Environment.TickCount64 + waitMs;
        var joined = true;
        foreach (var thread in _threads)
        {
            joined &= thread.Join((int)Math.Max(0, deadline - Environment.TickCount64));
        }

        if (joined)
        {
            foreach (var instance in _instances)
            {
                instance.Dispose();
            }
        }

        _threads.Clear();
        _instances.Clear();
    }

    public void Dispose() => Stop(500);
}
