using AgentSpeed.Core.Traffic.Helper;
using AgentSpeed.TrafficHelper.Counting;

namespace AgentSpeed.TrafficHelper.Pipe;

/// <summary>What the connection does after sending a response.</summary>
internal enum AfterResponse
{
    Continue,
    Close,
}

/// <summary>
/// Answers one request (WINDOWS-SERVICE 4): malformed → badRequest and close; more than 4 per second → rateLimited (and
/// close after 8 rejections in a row); a
/// version outside the supported range → unsupportedVersion with the hello payload; hello → hello; snapshot → the
/// client session's entries. Responses go into a buffer the connection reuses; its size depends only on the table.
/// </summary>
internal sealed class RequestHandler(TrafficTable table, HelperStats stats, ulong epoch, Func<ulong> fileTimeNow)
{
    /// <summary>The bytes received for one request: exactly 16, fewer when the read timed out, 17 when more followed.</summary>
    public AfterResponse Handle(
        ReadOnlySpan<byte> request, uint clientSession, long nowMs, RequestRateLimiter limiter, ref byte[] buffer, out int length)
    {
        Ensure(ref buffer, HelperProtocol.HeaderSize);
        var check = HelperRequest.TryParse(request, out var parsed);
        var type = (ushort)((ushort)parsed.Type & ~HelperProtocol.ErrorFlag);
        if (check == HelperRequestCheck.BadRequest)
        {
            length = WriteHeader(buffer, (ushort)(type | HelperProtocol.ErrorFlag), HelperStatusCode.BadRequest, parsed.Sequence, 0);
            return AfterResponse.Close;
        }

        if (!limiter.TryAcquire(nowMs))
        {
            length = WriteHeader(buffer, (ushort)(type | HelperProtocol.ErrorFlag), HelperStatusCode.RateLimited, parsed.Sequence, 0);
            return limiter.ConsecutiveRejections >= RequestRateLimiter.MaxConsecutiveRejections ? AfterResponse.Close : AfterResponse.Continue;
        }

        if (check == HelperRequestCheck.UnsupportedVersion)
        {
            length = WriteHello(ref buffer, (ushort)(type | HelperProtocol.ErrorFlag), HelperStatusCode.UnsupportedVersion, parsed.Sequence);
            return AfterResponse.Continue;
        }

        length = parsed.Type == HelperRequestType.Hello
            ? WriteHello(ref buffer, type, HelperStatusCode.Ok, parsed.Sequence)
            : WriteSnapshot(ref buffer, clientSession, parsed.Sequence);
        return AfterResponse.Continue;
    }

    private int WriteHello(ref byte[] buffer, ushort type, HelperStatusCode status, uint sequence)
    {
        Ensure(ref buffer, HelperProtocol.HeaderSize + HelperProtocol.HelloSize);
        WriteHeader(buffer, type, status, sequence, HelperProtocol.HelloSize);
        stats.Hello(table.EvictedEntries).Write(buffer.AsSpan(HelperProtocol.HeaderSize, HelperProtocol.HelloSize));
        return HelperProtocol.HeaderSize + HelperProtocol.HelloSize;
    }

    private int WriteSnapshot(ref byte[] buffer, uint clientSession, uint sequence)
    {
        const int entriesOffset = HelperProtocol.HeaderSize + HelperProtocol.SnapshotHeaderSize;
        Ensure(ref buffer, entriesOffset);
        var sampleTime = fileTimeNow();
        var count = table.WriteEntries(clientSession, ref buffer, entriesOffset);
        var payload = HelperSnapshot.PayloadLength(count);
        WriteHeader(buffer, (ushort)HelperRequestType.Snapshot, HelperStatusCode.Ok, sequence, (uint)payload);
        HelperSnapshot.WriteHeader(buffer.AsSpan(HelperProtocol.HeaderSize, HelperProtocol.SnapshotHeaderSize), sampleTime, count);
        return HelperProtocol.HeaderSize + payload;
    }

    private int WriteHeader(byte[] buffer, ushort type, HelperStatusCode status, uint sequence, uint payloadLength)
    {
        new HelperResponseHeader(HelperProtocol.Version, type, status, sequence, epoch, payloadLength)
            .Write(buffer.AsSpan(0, HelperProtocol.HeaderSize));
        return HelperProtocol.HeaderSize;
    }

    private static void Ensure(ref byte[] buffer, int length)
    {
        if (buffer.Length < length)
        {
            Array.Resize(ref buffer, length);
        }
    }
}
