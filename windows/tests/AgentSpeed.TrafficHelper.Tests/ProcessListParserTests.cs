using System.Buffers.Binary;
using AgentSpeed.TrafficHelper.Counting;

namespace AgentSpeed.TrafficHelper.Tests;

public sealed class ProcessListParserTests
{
    private const int EntrySize = 256;

    [Fact]
    public void ReadsPidParentSessionAndCreateTime()
    {
        var buffer = Chain((0, 0, 0, 0), (4, 0, 0, 1), (1_234, 600, 2, 133_000_000_000_000_000));
        var processes = new List<ProcessInfo>();

        Assert.True(ProcessListParser.TryParse(buffer, processes));

        var process = Assert.Single(processes);
        Assert.Equal(new ProcessInfo(1_234, 133_000_000_000_000_000, 600, 2), process);
    }

    [Fact]
    public void NextOffsetBeyondTheBufferIsMalformed()
    {
        var buffer = Chain((100, 1, 1, 10), (200, 1, 1, 20));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 10_000);
        var processes = new List<ProcessInfo>();

        Assert.False(ProcessListParser.TryParse(buffer, processes));
    }

    [Fact]
    public void NextOffsetSmallerThanAnEntryIsMalformed()
    {
        var buffer = Chain((100, 1, 1, 10), (200, 1, 1, 20));
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 8);

        Assert.False(ProcessListParser.TryParse(buffer, []));
    }

    [Fact]
    public void TruncatedBufferIsMalformed()
    {
        Assert.False(ProcessListParser.TryParse(new byte[50], []));
    }

    [Fact]
    public void ProcessesWithoutCreateTimeAreSkipped()
    {
        var processes = new List<ProcessInfo>();

        Assert.True(ProcessListParser.TryParse(Chain((500, 1, 1, 0), (501, 1, 1, 5)), processes));

        Assert.Equal(501u, Assert.Single(processes).Pid);
    }

    private static byte[] Chain(params (ulong Pid, ulong Parent, uint Session, long Created)[] entries)
    {
        var buffer = new byte[entries.Length * EntrySize];
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = buffer.AsSpan(i * EntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, i == entries.Length - 1 ? 0u : EntrySize);
            BinaryPrimitives.WriteInt64LittleEndian(entry[ProcessListParser.CreateTimeOffset..], entries[i].Created);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[ProcessListParser.UniqueProcessIdOffset..], entries[i].Pid);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[ProcessListParser.ParentProcessIdOffset..], entries[i].Parent);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[ProcessListParser.SessionIdOffset..], entries[i].Session);
        }

        return buffer;
    }
}
