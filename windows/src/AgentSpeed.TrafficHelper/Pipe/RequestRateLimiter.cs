namespace AgentSpeed.TrafficHelper.Pipe;

/// <summary>
/// At most 4 served requests in any 1 s window of one connection (WINDOWS-SERVICE 4). A client that keeps sending into
/// the limit (<see cref="MaxConsecutiveRejections"/> rejections in a row) is disconnected.
/// </summary>
internal sealed class RequestRateLimiter
{
    public const int MaxRequests = 4;
    public const long WindowMs = 1_000;
    public const int MaxConsecutiveRejections = 8;

    private readonly long[] _recent = new long[MaxRequests];
    private int _count;
    private int _next;

    /// <summary>Rejections since the last served request.</summary>
    public int ConsecutiveRejections { get; private set; }

    public bool TryAcquire(long nowMs)
    {
        if (_count < MaxRequests)
        {
            _recent[(_next + _count) % MaxRequests] = nowMs;
            _count++;
            ConsecutiveRejections = 0;
            return true;
        }

        // _next is the oldest of the last four served requests.
        if (nowMs - _recent[_next] < WindowMs)
        {
            ConsecutiveRejections++;
            return false;
        }

        _recent[_next] = nowMs;
        _next = (_next + 1) % MaxRequests;
        ConsecutiveRejections = 0;
        return true;
    }

    public void Reset()
    {
        _count = 0;
        _next = 0;
        ConsecutiveRejections = 0;
    }
}
