using System.Runtime.InteropServices;
using AgentSpeed.TrafficHelper.Native;

namespace AgentSpeed.TrafficHelper.Counting;

/// <summary>
/// One <c>NtQuerySystemInformation(SystemProcessInformation)</c> listing (WINDOWS-SERVICE 3.4): no process handles are
/// opened. The buffer is freed after each read to keep the working set small; its last size seeds the next read.
/// </summary>
internal sealed unsafe class ProcessListReader
{
    private const uint MaxBufferSize = 64 * 1024 * 1024;

    private readonly List<ProcessInfo> _processes = new(1_024);
    private uint _size = 512 * 1024;

    /// <summary>The current processes, or null when the query failed; the list is reused by the next call.</summary>
    public List<ProcessInfo>? Read()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var size = _size;
            var buffer = NativeMemory.Alloc(size);
            try
            {
                uint needed = 0;
                var status = SystemLibraries.NtQuerySystemInformation(SystemLibraries.SystemProcessInformation, buffer, size, &needed);
                if (status == SystemLibraries.StatusInfoLengthMismatch)
                {
                    var next = Math.Max(needed, size) + 64 * 1024;
                    if (next > MaxBufferSize)
                    {
                        return null;
                    }

                    _size = next;
                    continue;
                }

                if (status < 0)
                {
                    return null;
                }

                var length = needed > 0 ? Math.Min(needed, size) : size;
                return ProcessListParser.TryParse(new ReadOnlySpan<byte>(buffer, (int)length), _processes) ? _processes : null;
            }
            finally
            {
                NativeMemory.Free(buffer);
            }
        }

        return null;
    }
}
