using AgentSpeed.Core.Traffic.Helper;

namespace AgentSpeed.TrafficHelper.Counting;

/// <summary>
/// The service's process table with cumulative byte counters (WINDOWS-SERVICE 3.4–3.6). Entries are keyed by (PID, create
/// time); an exited entry is kept 15 s; at most 16 384 entries, evicting exited entries first (oldest exit), then the least
/// recently updated. Bytes for a PID the table does not know yet (its start event is still in another CPU's buffer) wait
/// briefly as an orphan and are credited once the process appears. Thread-safe; times are an injected monotonic clock in
/// milliseconds plus FILETIMEs from the events.
/// </summary>
internal sealed class TrafficTable
{
    public const int Capacity = HelperProtocol.MaxEntries;
    public const long ExitedRetentionMs = 15_000;
    public const long OrphanLifetimeMs = 5_000;
    public const int OrphanCapacity = 1_024;

    private readonly Lock _sync = new();
    private readonly int _capacity;
    private Entry[] _entries;
    private int _highWater;
    private readonly Stack<int> _free = new();
    private readonly Dictionary<(uint Pid, ulong CreateTime), int> _byKey = new();
    private readonly Dictionary<uint, int> _currentByPid = new();
    private readonly Dictionary<uint, Orphan> _orphans = new();
    private int _generation;
    private int _exitedCount;
    private ulong _evicted;

    public TrafficTable(int capacity = Capacity)
    {
        _capacity = Math.Clamp(capacity, 1, Capacity);
        _entries = new Entry[Math.Min(_capacity, 1_024)];
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _byKey.Count;
            }
        }
    }

    /// <summary>Entries dropped because the table was full.</summary>
    public ulong EvictedEntries
    {
        get
        {
            lock (_sync)
            {
                return _evicted;
            }
        }
    }

    public bool HasOrphans
    {
        get
        {
            lock (_sync)
            {
                return _orphans.Count > 0;
            }
        }
    }

    /// <summary>A Kernel-Process start event (or a new process in a snapshot).</summary>
    public void ProcessStarted(in ProcessInfo process, long nowMs)
    {
        lock (_sync)
        {
            Upsert(process, nowMs);
        }
    }

    /// <summary>A Kernel-Process stop event; <paramref name="createTime"/> 0 when the event lacks it.</summary>
    public void ProcessStopped(uint pid, ulong createTime, ulong exitTime, long nowMs)
    {
        lock (_sync)
        {
            int index;
            if (createTime != 0)
            {
                if (!_byKey.TryGetValue((pid, createTime), out index))
                {
                    return;
                }
            }
            else if (!_currentByPid.TryGetValue(pid, out index))
            {
                return;
            }

            MarkExited(index, exitTime, nowMs);
        }
    }

    /// <summary>Adds one network event's bytes; <paramref name="eventTime"/> is its FILETIME timestamp.</summary>
    public void AddBytes(uint pid, bool receive, uint size, ulong eventTime, long nowMs)
    {
        lock (_sync)
        {
            if (_currentByPid.TryGetValue(pid, out var index))
            {
                ref var entry = ref _entries[index];
                if (entry.Exited && entry.ExitTime != 0 && eventTime > entry.ExitTime)
                {
                    // The PID belongs to a newer process whose start event has not arrived yet.
                    AddOrphan(pid, receive, size, eventTime, nowMs);
                    return;
                }

                if (eventTime != 0 && eventTime < entry.CreateTime)
                {
                    // A late event of an earlier process with this PID: no longer attributable.
                    return;
                }

                Credit(ref entry, receive, size, nowMs);
                return;
            }

            AddOrphan(pid, receive, size, eventTime, nowMs);
        }
    }

    /// <summary>
    /// Reconciles the table with a full process listing taken at <paramref name="snapshotTime"/> (FILETIME): new processes
    /// are added, live entries missing from it (and created before it) are marked exited.
    /// </summary>
    public void Reconcile(ReadOnlySpan<ProcessInfo> processes, ulong snapshotTime, long nowMs)
    {
        lock (_sync)
        {
            var generation = ++_generation;
            foreach (var process in processes)
            {
                var index = Upsert(process, nowMs);
                if (index >= 0)
                {
                    _entries[index].SeenGeneration = generation;
                }
            }

            for (var i = 0; i < _highWater; i++)
            {
                ref var entry = ref _entries[i];
                if (entry.Used && !entry.Exited && entry.SeenGeneration != generation && entry.CreateTime < snapshotTime)
                {
                    MarkExited(i, snapshotTime, nowMs);
                }
            }
        }
    }

    /// <summary>Drops exited entries after their retention and orphans that never found their process.</summary>
    public void Expire(long nowMs)
    {
        lock (_sync)
        {
            if (_exitedCount > 0)
            {
                for (var i = 0; i < _highWater; i++)
                {
                    ref var entry = ref _entries[i];
                    if (entry.Used && entry.Exited && nowMs - entry.ExitedMs >= ExitedRetentionMs)
                    {
                        Remove(i);
                    }
                }
            }

            if (_orphans.Count > 0)
            {
                List<uint>? stale = null;
                foreach (var (pid, orphan) in _orphans)
                {
                    if (nowMs - orphan.FirstSeenMs >= OrphanLifetimeMs)
                    {
                        (stale ??= []).Add(pid);
                    }
                }

                if (stale is not null)
                {
                    foreach (var pid in stale)
                    {
                        _orphans.Remove(pid);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Writes the snapshot entries (WINDOWS-SERVICE 4) of the processes in <paramref name="sessionId"/> that have counted
    /// bytes into <paramref name="buffer"/> at <paramref name="offset"/>, growing the buffer from the table's own size
    /// (never from client input). Returns the entry count.
    /// </summary>
    public int WriteEntries(uint sessionId, ref byte[] buffer, int offset)
    {
        lock (_sync)
        {
            var needed = offset + _byKey.Count * HelperProtocol.EntrySize;
            if (buffer.Length < needed)
            {
                Array.Resize(ref buffer, Math.Max(needed, Math.Min(buffer.Length * 2, offset + HelperProtocol.MaxEntries * HelperProtocol.EntrySize)));
            }

            var count = 0;
            for (var i = 0; i < _highWater && count < HelperProtocol.MaxEntries; i++)
            {
                ref var entry = ref _entries[i];
                if (!entry.Used || entry.SessionId != sessionId || (entry.Rx | entry.Tx) == 0)
                {
                    continue;
                }

                new HelperEntry(entry.Pid, entry.Exited ? HelperEntry.ExitedFlag : 0, entry.CreateTime, entry.Rx, entry.Tx)
                    .Write(buffer.AsSpan(offset + count * HelperProtocol.EntrySize, HelperProtocol.EntrySize));
                count++;
            }

            return count;
        }
    }

    /// <summary>Test and diagnostics view of one entry.</summary>
    public bool TryGet(uint pid, ulong createTime, out TrafficEntryView view)
    {
        lock (_sync)
        {
            view = default;
            if (!_byKey.TryGetValue((pid, createTime), out var index))
            {
                return false;
            }

            ref var entry = ref _entries[index];
            view = new TrafficEntryView(entry.Pid, entry.CreateTime, entry.SessionId, entry.Rx, entry.Tx, entry.Exited);
            return true;
        }
    }

    private int Upsert(in ProcessInfo process, long nowMs)
    {
        if (process.Pid <= 4 || process.CreateTime == 0)
        {
            return -1;
        }

        if (_byKey.TryGetValue((process.Pid, process.CreateTime), out var existing))
        {
            ref var known = ref _entries[existing];
            known.ParentPid = process.ParentPid;
            known.SessionId = process.SessionId;
            if (!known.Exited)
            {
                _currentByPid[process.Pid] = existing;
            }

            return existing;
        }

        if (_currentByPid.TryGetValue(process.Pid, out var previous))
        {
            var older = _entries[previous].CreateTime < process.CreateTime;
            if (!older)
            {
                // A listing can be older than an event already applied; never replace a newer process with an older one.
                return -1;
            }

            MarkExited(previous, process.CreateTime, nowMs);
        }

        var index = Allocate(nowMs);
        _entries[index] = new Entry
        {
            Used = true,
            Pid = process.Pid,
            CreateTime = process.CreateTime,
            ParentPid = process.ParentPid,
            SessionId = process.SessionId,
            UpdatedMs = nowMs,
        };
        _byKey[(process.Pid, process.CreateTime)] = index;
        _currentByPid[process.Pid] = index;

        if (_orphans.Remove(process.Pid, out var orphan) && orphan.FirstEventTime >= process.CreateTime)
        {
            ref var entry = ref _entries[index];
            entry.Rx += orphan.Rx;
            entry.Tx += orphan.Tx;
        }

        return index;
    }

    private void Credit(ref Entry entry, bool receive, uint size, long nowMs)
    {
        if (receive)
        {
            entry.Rx += size;
        }
        else
        {
            entry.Tx += size;
        }

        entry.UpdatedMs = nowMs;
    }

    private void AddOrphan(uint pid, bool receive, uint size, ulong eventTime, long nowMs)
    {
        if (!_orphans.TryGetValue(pid, out var orphan))
        {
            if (_orphans.Count >= OrphanCapacity)
            {
                return;
            }

            orphan = new Orphan { FirstEventTime = eventTime, FirstSeenMs = nowMs };
        }

        if (receive)
        {
            orphan.Rx += size;
        }
        else
        {
            orphan.Tx += size;
        }

        orphan.FirstEventTime = Math.Min(orphan.FirstEventTime, eventTime);
        _orphans[pid] = orphan;
    }

    private void MarkExited(int index, ulong exitTime, long nowMs)
    {
        ref var entry = ref _entries[index];
        if (entry.Exited)
        {
            return;
        }

        entry.Exited = true;
        entry.ExitedMs = nowMs;
        entry.ExitTime = exitTime;
        _exitedCount++;
    }

    private int Allocate(long nowMs)
    {
        if (_byKey.Count >= _capacity)
        {
            Evict();
        }

        if (_free.Count > 0)
        {
            return _free.Pop();
        }

        if (_highWater == _entries.Length)
        {
            Array.Resize(ref _entries, Math.Min(_capacity, _entries.Length * 2));
        }

        return _highWater++;
    }

    /// <summary>Drops the exited entry that exited first, else the least recently updated entry.</summary>
    private void Evict()
    {
        var victim = -1;
        var victimExited = false;
        var victimTime = long.MaxValue;
        for (var i = 0; i < _highWater; i++)
        {
            ref var entry = ref _entries[i];
            if (!entry.Used)
            {
                continue;
            }

            if (entry.Exited)
            {
                if (!victimExited || entry.ExitedMs < victimTime)
                {
                    victim = i;
                    victimExited = true;
                    victimTime = entry.ExitedMs;
                }
            }
            else if (!victimExited && entry.UpdatedMs < victimTime)
            {
                victim = i;
                victimTime = entry.UpdatedMs;
            }
        }

        if (victim >= 0)
        {
            Remove(victim);
            _evicted++;
        }
    }

    private void Remove(int index)
    {
        ref var entry = ref _entries[index];
        _byKey.Remove((entry.Pid, entry.CreateTime));
        if (_currentByPid.TryGetValue(entry.Pid, out var current) && current == index)
        {
            _currentByPid.Remove(entry.Pid);
        }

        if (entry.Exited)
        {
            _exitedCount--;
        }

        entry = default;
        _free.Push(index);
    }

    private struct Entry
    {
        public bool Used;
        public bool Exited;
        public uint Pid;
        public uint ParentPid;
        public uint SessionId;
        public int SeenGeneration;
        public ulong CreateTime;
        public ulong ExitTime;
        public ulong Rx;
        public ulong Tx;
        public long UpdatedMs;
        public long ExitedMs;
    }

    private struct Orphan
    {
        public ulong Rx;
        public ulong Tx;
        public ulong FirstEventTime;
        public long FirstSeenMs;
    }
}

/// <summary>A read-only copy of one table entry.</summary>
internal readonly record struct TrafficEntryView(uint Pid, ulong CreateTime, uint SessionId, ulong Rx, ulong Tx, bool Exited);
