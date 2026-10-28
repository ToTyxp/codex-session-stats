using AgentSpeed.TrafficHelper.Counting;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class TrafficTableTests
{
    private readonly TrafficTable _table = new();

    [Fact]
    public void CountsReceiveAndSendSeparately()
    {
        _table.ProcessStarted(new ProcessInfo(1234, 100, 1, 1), 0);
        _table.AddBytes(1234, receive: true, 1000, 200, 10);
        _table.AddBytes(1234, receive: true, 500, 210, 20);
        _table.AddBytes(1234, receive: false, 70, 220, 30);

        Assert.True(_table.TryGet(1234, 100, out var entry));
        Assert.Equal(1500ul, entry.Rx);
        Assert.Equal(70ul, entry.Tx);
        Assert.False(entry.Exited);
    }

    [Fact]
    public void IdleAndSystemProcessesAreNeverTracked()
    {
        _table.ProcessStarted(new ProcessInfo(4, 100, 0, 0), 0);
        _table.ProcessStarted(new ProcessInfo(0, 100, 0, 0), 0);

        Assert.Equal(0, _table.Count);
    }

    [Fact]
    public void ExitedEntryIsKeptFifteenSecondsThenDropped()
    {
        _table.ProcessStarted(new ProcessInfo(50, 100, 1, 1), 0);
        _table.AddBytes(50, receive: true, 10, 150, 0);
        _table.ProcessStopped(50, 100, 300, 1_000);

        _table.Expire(1_000 + TrafficTable.ExitedRetentionMs - 1);
        Assert.True(_table.TryGet(50, 100, out var kept));
        Assert.True(kept.Exited);
        Assert.Equal(10ul, kept.Rx);

        _table.Expire(1_000 + TrafficTable.ExitedRetentionMs);
        Assert.False(_table.TryGet(50, 100, out _));
        Assert.Equal(0ul, _table.EvictedEntries);
    }

    [Fact]
    public void LateEventOfAnExitedProcessIsStillCounted()
    {
        _table.ProcessStarted(new ProcessInfo(50, 100, 1, 1), 0);
        _table.ProcessStopped(50, 100, 500, 10);
        _table.AddBytes(50, receive: true, 64, 400, 20);

        Assert.True(_table.TryGet(50, 100, out var entry));
        Assert.Equal(64ul, entry.Rx);
    }

    [Fact]
    public void BytesBeforeTheStartEventWaitAsOrphanThenAreCredited()
    {
        _table.AddBytes(77, receive: false, 300, 1_005, 0);
        Assert.True(_table.HasOrphans);

        _table.ProcessStarted(new ProcessInfo(77, 1_000, 1, 1), 50);

        Assert.False(_table.HasOrphans);
        Assert.True(_table.TryGet(77, 1_000, out var entry));
        Assert.Equal(300ul, entry.Tx);
    }

    [Fact]
    public void ReusedPidAfterExitStartsAFreshEntry()
    {
        _table.ProcessStarted(new ProcessInfo(77, 1_000, 1, 1), 0);
        _table.AddBytes(77, receive: true, 10, 1_100, 0);
        _table.ProcessStopped(77, 1_000, 2_000, 10);

        // The new process's first event arrives before its start event.
        _table.AddBytes(77, receive: true, 900, 3_100, 20);
        _table.ProcessStarted(new ProcessInfo(77, 3_000, 1, 1), 30);

        Assert.True(_table.TryGet(77, 1_000, out var old));
        Assert.Equal(10ul, old.Rx);
        Assert.True(_table.TryGet(77, 3_000, out var fresh));
        Assert.Equal(900ul, fresh.Rx);
        Assert.False(fresh.Exited);
    }

    [Fact]
    public void OrphanOlderThanItsProcessIsDropped()
    {
        _table.AddBytes(88, receive: true, 500, 900, 0);
        _table.ProcessStarted(new ProcessInfo(88, 1_000, 1, 1), 10);

        Assert.True(_table.TryGet(88, 1_000, out var entry));
        Assert.Equal(0ul, entry.Rx);
    }

    [Fact]
    public void OrphanThatNeverFindsItsProcessExpires()
    {
        _table.AddBytes(99, receive: true, 500, 900, 0);

        _table.Expire(TrafficTable.OrphanLifetimeMs - 1);
        Assert.True(_table.HasOrphans);
        _table.Expire(TrafficTable.OrphanLifetimeMs);
        Assert.False(_table.HasOrphans);

        _table.ProcessStarted(new ProcessInfo(99, 800, 1, 1), TrafficTable.OrphanLifetimeMs + 1);
        Assert.True(_table.TryGet(99, 800, out var entry));
        Assert.Equal(0ul, entry.Rx);
    }

    [Fact]
    public void OrphansAreBounded()
    {
        for (uint pid = 10; pid < 10 + TrafficTable.OrphanCapacity + 10; pid++)
        {
            _table.AddBytes(pid, receive: true, 1, 100, 0);
        }

        _table.ProcessStarted(new ProcessInfo(10 + TrafficTable.OrphanCapacity + 5, 50, 1, 1), 1);
        Assert.True(_table.TryGet(10 + TrafficTable.OrphanCapacity + 5, 50, out var beyond));
        Assert.Equal(0ul, beyond.Rx);
        _table.ProcessStarted(new ProcessInfo(10, 50, 1, 1), 1);
        Assert.True(_table.TryGet(10, 50, out var within));
        Assert.Equal(1ul, within.Rx);
    }

    [Fact]
    public void EventBeforeTheCurrentProcessWasCreatedIsIgnored()
    {
        _table.ProcessStarted(new ProcessInfo(60, 5_000, 1, 1), 0);
        _table.AddBytes(60, receive: true, 100, 4_000, 10);

        Assert.True(_table.TryGet(60, 5_000, out var entry));
        Assert.Equal(0ul, entry.Rx);
        Assert.False(_table.HasOrphans);
    }

    [Fact]
    public void ReconcileAddsNewProcessesAndMarksVanishedOnesExited()
    {
        _table.ProcessStarted(new ProcessInfo(10, 100, 1, 1), 0);
        _table.ProcessStarted(new ProcessInfo(11, 100, 1, 1), 0);
        // Created after the listing was taken: must survive although it is absent.
        _table.ProcessStarted(new ProcessInfo(12, 9_000, 1, 1), 0);

        ProcessInfo[] listing = [new(10, 100, 1, 1), new(13, 200, 10, 2)];
        _table.Reconcile(listing, 5_000, 100);

        Assert.True(_table.TryGet(10, 100, out var alive) && !alive.Exited);
        Assert.True(_table.TryGet(11, 100, out var vanished) && vanished.Exited);
        Assert.True(_table.TryGet(12, 9_000, out var newer) && !newer.Exited);
        Assert.True(_table.TryGet(13, 200, out var added) && added.SessionId == 2);
    }

    [Fact]
    public void ReconcileReplacesAProcessWhoseStopEventWasMissed()
    {
        _table.ProcessStarted(new ProcessInfo(20, 100, 1, 1), 0);
        _table.Reconcile([new ProcessInfo(20, 700, 1, 1)], 1_000, 50);

        Assert.True(_table.TryGet(20, 100, out var old) && old.Exited);
        Assert.True(_table.TryGet(20, 700, out var current) && !current.Exited);
        _table.AddBytes(20, receive: true, 5, 800, 60);
        Assert.True(_table.TryGet(20, 700, out current) && current.Rx == 5);
    }

    [Fact]
    public void ReconcileNeverReplacesANewerProcessWithAnOlderListing()
    {
        // The listing was taken (at 500) before the process now holding PID 20 was created (at 900).
        _table.ProcessStarted(new ProcessInfo(20, 900, 1, 1), 0);
        _table.Reconcile([new ProcessInfo(20, 100, 1, 1)], 500, 50);

        Assert.True(_table.TryGet(20, 900, out var current) && !current.Exited);
        Assert.False(_table.TryGet(20, 100, out _));
    }

    [Fact]
    public void FullTableEvictsTheOldestExitedEntryFirst()
    {
        var table = new TrafficTable(capacity: 3);
        table.ProcessStarted(new ProcessInfo(10, 1, 1, 1), 0);
        table.ProcessStarted(new ProcessInfo(11, 1, 1, 1), 0);
        table.ProcessStarted(new ProcessInfo(12, 1, 1, 1), 0);
        table.ProcessStopped(11, 1, 5, 200);
        table.ProcessStopped(12, 1, 5, 100);

        table.ProcessStarted(new ProcessInfo(13, 1, 1, 1), 300);

        Assert.True(table.TryGet(10, 1, out _));
        Assert.True(table.TryGet(11, 1, out _));
        Assert.False(table.TryGet(12, 1, out _));
        Assert.True(table.TryGet(13, 1, out _));
        Assert.Equal(1ul, table.EvictedEntries);
    }

    [Fact]
    public void FullTableWithoutExitedEntriesEvictsTheLeastRecentlyUpdated()
    {
        var table = new TrafficTable(capacity: 3);
        table.ProcessStarted(new ProcessInfo(10, 1, 1, 1), 0);
        table.ProcessStarted(new ProcessInfo(11, 1, 1, 1), 10);
        table.ProcessStarted(new ProcessInfo(12, 1, 1, 1), 20);
        table.AddBytes(10, receive: true, 1, 2, 30);

        table.ProcessStarted(new ProcessInfo(13, 1, 1, 1), 40);
        Assert.False(table.TryGet(11, 1, out _));

        table.ProcessStarted(new ProcessInfo(14, 1, 1, 1), 50);
        Assert.False(table.TryGet(12, 1, out _));
        Assert.True(table.TryGet(10, 1, out _));
        Assert.Equal(2ul, table.EvictedEntries);
        Assert.Equal(3, table.Count);
    }

    [Fact]
    public void EvictedPidSlotIsReusedCleanly()
    {
        var table = new TrafficTable(capacity: 2);
        table.ProcessStarted(new ProcessInfo(10, 1, 1, 1), 0);
        table.AddBytes(10, receive: true, 7, 2, 0);
        table.ProcessStarted(new ProcessInfo(11, 1, 1, 1), 5);
        table.ProcessStarted(new ProcessInfo(12, 1, 1, 1), 10);

        Assert.False(table.TryGet(10, 1, out _));
        table.AddBytes(10, receive: true, 7, 3, 20);
        Assert.True(table.HasOrphans);
        Assert.True(table.TryGet(12, 1, out var fresh));
        Assert.Equal(0ul, fresh.Rx);
    }
}
