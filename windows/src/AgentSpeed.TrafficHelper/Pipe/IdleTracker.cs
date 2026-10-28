namespace AgentSpeed.TrafficHelper.Pipe;

/// <summary>Idle stop (WINDOWS-SERVICE 3.7): the service exits when no client has been connected for 120 s. Thread-safe.</summary>
internal sealed class IdleTracker(long startMs)
{
    public const long IdleStopMs = 120_000;

    private readonly Lock _sync = new();
    private int _clients;
    private long _idleSinceMs = startMs;

    public int Clients
    {
        get
        {
            lock (_sync)
            {
                return _clients;
            }
        }
    }

    public void Connected()
    {
        lock (_sync)
        {
            _clients++;
        }
    }

    public void Disconnected(long nowMs)
    {
        lock (_sync)
        {
            if (_clients > 0 && --_clients == 0)
            {
                _idleSinceMs = nowMs;
            }
        }
    }

    public bool ShouldStop(long nowMs)
    {
        lock (_sync)
        {
            return _clients == 0 && nowMs - _idleSinceMs >= IdleStopMs;
        }
    }
}
