namespace AgentSpeed.TrafficHelper.Counting;

/// <summary>One process as the process table knows it: identity (PID, create time FILETIME), parent and session.</summary>
internal readonly record struct ProcessInfo(uint Pid, ulong CreateTime, uint ParentPid, uint SessionId);
