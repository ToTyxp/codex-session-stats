namespace AgentSpeed.TrafficHelper.Pipe;

/// <summary>
/// Which connections may be served. Session 0 is refused: an allowed user's batch or service logon lands there and would
/// otherwise see the services' and other users' session-0 processes. Each session may hold at most
/// <see cref="PerSessionLimit"/> of the four pipe instances, so one session cannot lock the others out. Thread-safe.
/// </summary>
internal sealed class SessionGate(int perSessionLimit = SessionGate.PerSessionLimit)
{
    public const int PerSessionLimit = 2;

    private readonly Lock _sync = new();
    private readonly Dictionary<uint, int> _connections = new();

    public bool TryEnter(uint sessionId)
    {
        if (sessionId == 0)
        {
            return false;
        }

        lock (_sync)
        {
            _connections.TryGetValue(sessionId, out var count);
            if (count >= perSessionLimit)
            {
                return false;
            }

            _connections[sessionId] = count + 1;
            return true;
        }
    }

    public void Leave(uint sessionId)
    {
        lock (_sync)
        {
            if (!_connections.TryGetValue(sessionId, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _connections.Remove(sessionId);
            }
            else
            {
                _connections[sessionId] = count - 1;
            }
        }
    }

    public int Count(uint sessionId)
    {
        lock (_sync)
        {
            return _connections.TryGetValue(sessionId, out var count) ? count : 0;
        }
    }
}
