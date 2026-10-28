using System.Buffers.Binary;

namespace AgentSpeed.TrafficHelper.Counting;

/// <summary>
/// Parses the 64-bit <c>SYSTEM_PROCESS_INFORMATION</c> chain that <c>NtQuerySystemInformation(SystemProcessInformation)</c>
/// returns: per entry only PID, parent PID, session and create time are read (WINDOWS-SERVICE 3.4). Bounds-checked; a
/// malformed chain ends the walk and is reported as false.
/// </summary>
internal static class ProcessListParser
{
    public const int NextEntryOffset = 0;
    public const int CreateTimeOffset = 32;
    public const int UniqueProcessIdOffset = 80;
    public const int ParentProcessIdOffset = 88;
    public const int SessionIdOffset = 100;
    public const int MinimumEntrySize = 104;
    public const int MaximumEntries = 1 << 16;

    public static bool TryParse(ReadOnlySpan<byte> buffer, List<ProcessInfo> processes)
    {
        processes.Clear();
        var offset = 0;
        for (var guard = 0; guard < MaximumEntries; guard++)
        {
            if (offset < 0 || buffer.Length - offset < MinimumEntrySize)
            {
                return false;
            }

            var entry = buffer[offset..];
            var next = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            var pid = BinaryPrimitives.ReadUInt64LittleEndian(entry[UniqueProcessIdOffset..]);
            var createTime = BinaryPrimitives.ReadInt64LittleEndian(entry[CreateTimeOffset..]);
            if (pid > 4 && pid <= uint.MaxValue && createTime > 0)
            {
                var parent = BinaryPrimitives.ReadUInt64LittleEndian(entry[ParentProcessIdOffset..]);
                processes.Add(new ProcessInfo(
                    (uint)pid,
                    (ulong)createTime,
                    parent <= uint.MaxValue ? (uint)parent : 0,
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[SessionIdOffset..])));
            }

            if (next == 0)
            {
                return true;
            }

            if (next < MinimumEntrySize || next > buffer.Length - offset)
            {
                return false;
            }

            offset += (int)next;
        }

        return false;
    }
}
